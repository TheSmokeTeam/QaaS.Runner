"""Verify a provisioned Grafana/Prometheus stack using disposable loopback services."""
import argparse
import base64
from contextlib import ExitStack
from datetime import datetime,timezone
import json
import math
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import tempfile
import time
from urllib.error import URLError
from urllib.parse import urlencode
from urllib.request import ProxyHandler, Request, build_opener

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from readmodel import METRICS

G,P,E="http://127.0.0.1:13001","http://127.0.0.1:19091","http://127.0.0.1:19333"
HTTP=build_opener(ProxyHandler({}))
CUTOFF="2026-11-22T16:00:00Z"
METRIC=re.compile(r'qaas_runner_kpi_(\w+)\{window="([^"]+)",team="([^"]+)",environment="([^"]+)"\} (\S+)')


def request(url, *, body=None, password=None):
    headers={"Content-Type":"application/json"}
    if password:
        headers["Authorization"]="Basic "+base64.b64encode(f"admin:{password}".encode()).decode()
    with HTTP.open(Request(url,headers=headers,data=json.dumps(body).encode() if body is not None else None),timeout=8) as result:
        return result.read()


def api(url, **kwargs):
    return json.loads(request(url,**kwargs))


def wait_for(procs,test):
    finish=time.monotonic()+35
    while time.monotonic()<finish:
        if any(p.poll() is not None for p in procs):
            raise RuntimeError("a disposable service exited before verification")
        try:
            if test():
                return
        except (URLError,TimeoutError):
            pass
        time.sleep(.2)
    raise TimeoutError("disposable loopback service not ready")


def check_views(password,metric_snapshot):
    uid="qaas-runner-terminal-kpi"
    dashboard=api(G+"/api/dashboards/uid/"+uid,password=password)["dashboard"]
    panels=[p for p in dashboard["panels"] if p["type"]=="stat"]
    assert len(panels)==len(METRICS)==len({p["id"] for p in panels})
    samples={}
    for match in METRIC.finditer(metric_snapshot):
        metric,window,team,env,val=match.groups()
        samples.setdefault((window,team,env),{})[metric]=float(val)
    assert len(samples)==18
    checks=0
    undefined=0
    for scope,row in sorted(samples.items()):
        queries=[]
        expected={}
        for panel in panels:
            target=panel["targets"][0]
            expr=target["expr"]
            for name,value in zip(("window","team","environment"),scope):
                expr=expr.replace("$"+name,value)
            metric=re.search(r"qaas_runner_kpi_(\w+)",expr)[1]
            value=row.get(metric)
            if value is not None and expr.endswith(" * 1000"):
                value*=1000
            ref="Panel"+str(panel["id"])
            queries.append(dict(target,expr=expr,refId=ref))
            expected[ref]=value
        now=int(time.time()*1000)
        result=api(G+"/api/ds/query",password=password,
                   body={"from":str(now-300000),"to":str(now),"queries":queries})["results"]
        assert result.keys()==expected.keys()
        for ref,value in expected.items():
            response=result[ref]
            assert not response.get("error"),(scope,ref,response)
            frames=response.get("frames",[])
            frames_data=[f["data"]["values"] for f in frames]
            if value is None:
                assert all(not v or not v[0] for v in frames_data),(scope,ref,frames)
                undefined+=1
            else:
                assert len(frames_data)==1 and len(frames_data[0][1])==1,(scope,ref,frames)
                assert math.isclose(float(frames_data[0][1][0]),value),(scope,ref,value,frames)
                for field in frames[0]["schema"]["fields"]:
                    assert set(field.get("labels",{}))<={"__name__","instance","job","environment","team","window"}
            checks+=1
    return {"grafana":api(G+"/api/health")["version"],
            "prometheus":api(P+"/api/v1/status/buildinfo")["data"]["version"],
            "actual_provisioned_dashboard_uid":uid,"panels":len(panels),"scopes":len(samples),
            "grafana_frame_cells":checks,"undefined_cells":undefined}


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--grafana-home",required=True,type=Path)
    p.add_argument("--prometheus",required=True,type=Path)
    p.add_argument("--metrics",type=Path,help="Existing materialized approved snapshot; otherwise use known fixture.")
    a=p.parse_args()
    # This executable never launches services on an existing network namespace.
    interfaces={x.split(":")[0].strip() for x in Path("/proc/net/dev").read_text().splitlines()[2:]}
    if interfaces!={"lo"}:
        p.error("use the private loopback namespace in README.md")
    password=secrets.token_urlsafe(32)
    processes=[]
    with tempfile.TemporaryDirectory(prefix="qaas-runner-kpi-") as state,ExitStack() as logs:
        d=Path(state)
        def launch(name,cmd,env=None):
            stream=logs.enter_context((d/(name+".log")).open("w+"))
            proc=subprocess.Popen(cmd,cwd=ROOT,env=env,stdout=stream,stderr=subprocess.STDOUT)
            processes.append(proc)
            return stream
        snapshot=a.metrics.resolve() if a.metrics else d/"snapshot.prom"
        if not a.metrics:
            subprocess.run([sys.executable,str(ROOT/"readmodel.py"),"--accepted",str(ROOT/"fixtures/accepted.jsonl"),
                            "--roster",str(ROOT/"fixtures/roster.json"),"--ledger",str(ROOT/"fixtures/ledger.json"),
                            "--as-of",CUTOFF,"--output",str(snapshot)],check=True)
        # Browser-independent API verifier: import is performed by the actual
        # Grafana dashboard provider, rather than a mocked UI or local parser.
        try:
            launch("exporter",[sys.executable,str(ROOT/"serve.py"),"--metrics",str(snapshot)])
            wait_for(processes,lambda:request(E+"/metrics"))
            launch("prometheus",[str(a.prometheus.resolve()),f"--config.file={ROOT/'grafana/prometheus.yml'}",
                                 f"--storage.tsdb.path={d/'prom'}","--web.listen-address=127.0.0.1:19091"])
            wait_for(processes,lambda:api(P+"/api/v1/query?"+urlencode(
                {"query":"qaas_runner_kpi_eligible_projects"}))["data"]["result"])
            env={k:v for k,v in os.environ.items() if k in ("PATH","HOME","LANG")}
            env.update(GF_SERVER_HTTP_ADDR="127.0.0.1",GF_SERVER_HTTP_PORT="13001",
                GF_PATHS_DATA=str(d/"data"),GF_PATHS_LOGS=str(d/"logs"),GF_PATHS_PLUGINS=str(d/"plugins"),
                GF_PATHS_PROVISIONING=str(ROOT/"grafana/provisioning"),
                QAAS_KPI_DASHBOARD_DIR=str(ROOT/"grafana/dashboards"),GF_SECURITY_ADMIN_PASSWORD=password,
                GF_ANALYTICS_REPORTING_ENABLED="false",GF_ANALYTICS_CHECK_FOR_UPDATES="false",
                GF_ANALYTICS_CHECK_FOR_PLUGIN_UPDATES="false",GF_PLUGINS_PUBLIC_KEY_RETRIEVAL_DISABLED="true",
                GF_PLUGINS_PREINSTALL_DISABLED="true",GF_PLUGINS_PLUGIN_ADMIN_ENABLED="false",
                GF_SECURITY_DISABLE_GRAVATAR="true",GF_NEWS_NEWS_FEED_ENABLED="false",GF_LOG_MODE="console")
            home=a.grafana_home.resolve()
            launch("grafana",[str(home/"bin/grafana"),"server","--homepath",str(home)],env)
            wait_for(processes, lambda:api(G+"/api/dashboards/uid/qaas-runner-terminal-kpi",password=password))
            evidence=check_views(password,snapshot.read_text())
            print(json.dumps(evidence,sort_keys=True))
        except Exception:
            for name in ("exporter","prometheus","grafana"):
                file=d/(name+".log")
                if file.exists():
                    print(f"{name} diagnostic:\n"+file.read_text()[-3000:],file=sys.stderr)
            raise
        finally:
            for proc in reversed(processes):
                proc.terminate()
                try:
                    proc.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait(timeout=5)


if __name__=="__main__":
    main()

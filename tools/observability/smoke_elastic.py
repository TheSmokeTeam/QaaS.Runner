"""Opt-in integration check against a disposable, deliberately named loopback node."""
import argparse
import json
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path
from uuid import uuid4

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--accepted-jsonl', type=Path)
args = parser.parse_args()
folder = Path(__file__).resolve().parent
origin = 'http://127.0.0.1:19256'
index = 'qaas-attempts-v1-'+uuid4().hex[:8]
mapping = json.loads((folder/'index-mapping.json').read_text())
mapping['settings'] = {'number_of_shards': 1,'number_of_replicas':0}

def req(method,path,data=None):
    req=urllib.request.Request(origin+path,json.dumps(data).encode() if data else None,
        {'Content-Type':'application/json'}, method=method)
    with urllib.request.urlopen(req,timeout=8) as r:
        return json.load(r)

def case(i,outcome='pass',duration=1):
    return {'schema_version':1,'attempt_id':hex(i)[2:]*32,'project_key':'c'*32,
            'operation':'run','outcome':outcome,'completed_at':'2026-11-22T12:00:00.1234567Z',
            'duration_ms':duration,'exit_code':0 if outcome=='pass' else 1}

node = req('GET', '/')
if node['version']['number'] != '8.15.5' or node['cluster_name'] != 'qaas-collector-synthetic':
    raise SystemExit('Expected disposable loopback cluster qaas-collector-synthetic, Elasticsearch 8.15.5')
req('PUT','/'+index,mapping)
with tempfile.TemporaryDirectory() as temp:
    spool=Path(temp)
    def emit(v):
        p=spool/(v['attempt_id']+'.json')
        p.write_text(json.dumps(v))
    def run(expected=0,target=index):
        p=subprocess.run([sys.executable,str(folder/'collect.py'),'--spool',str(spool),
                          '--elastic',origin,'--allow-loopback-http','--index',target],
                          text=True,capture_output=True)
        assert p.returncode==expected,(p.returncode,p.stdout,p.stderr)
        return json.loads(p.stdout) if p.stdout else None

    emit(case(1)); emit(case(2,'assertion_failure'))
    a=run(); assert a['created']==2,a
    emit(case(1)); b=run(); assert b['duplicate']==1,b
    emit(case(1,duration=2)); c=run(3); assert c['conflict']==1,c
    req('PUT','/'+index+'-new',mapping)
    assert run(1,index+'-new') is None
    assert (spool/('1'*32+'.json')).is_file()
    req('POST','/'+index+'/_refresh')
    q=req('POST','/'+index+'/_search',{'size':2,'track_total_hits':True,
          'aggs':{'by_outcome':{'terms':{'field':'outcome'}}}})
    agg={v['key']:v['doc_count'] for v in q['aggregations']['by_outcome']['buckets']}
    assert q['hits']['total']=={'value':2,'relation':'eq'} and agg=={'pass':1,'assertion_failure':1}
    accepted = sorted((hit['_source'] for hit in q['hits']['hits']), key=lambda row: row['attempt_id'])
    assert accepted == [case(1), case(2, 'assertion_failure')], accepted
if args.accepted_jsonl:
    with args.accepted_jsonl.open('x', encoding='utf-8') as output:
        for row in accepted:
            output.write(json.dumps(row, separators=(',', ':')) + '\n')
print(json.dumps({'elastic':'8.15.5','created':a,'retry':b,'conflict':c,'route_change_blocked':True,
                  'accepted_read_model_query':{'total':2,'by_outcome':agg}},indent=2))

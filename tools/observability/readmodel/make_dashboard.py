"""Generate a stock Grafana dashboard from the independently governed scope allowlists."""
import argparse
import json
from pathlib import Path

from readmodel import METRICS, atomic, roster, tick

DS={"type":"prometheus","uid":"qaas-runner-kpi"}
GROUPS=(
 ("Observed adoption",(
  ("Observed eligible adoption","adoption_ratio","percentunit"),("Active eligible projects","active_projects","short"),
  ("Eligible projects","eligible_projects","short"),("Enrolled eligible projects","enrolled_projects","short"),
  ("Eligible terminal attempts","attempts","short"),("Eligible terminals/day","throughput_per_day","short"),
  ("Unrostered attempts (all/all only)","unrostered_attempts","short"),
  ("Outside current cohort (all/all only)","out_of_cohort_attempts","short"),
 )),
 ("Explicit CLI operation",(
  ("Run operations","operation_run_attempts","short"),("Act operations","operation_act_attempts","short"),
  ("Assert operations","operation_assert_attempts","short"),
 )),
 ("Terminal outcomes and delay",(
  ("Passed","pass","short"),("Assertion failure","assertion_failure","short"),
  ("Infrastructure failure","infra_failure","short"),("Cancelled","cancelled","short"),
  ("Successful without test verdict","completed","short"),("Unknown outcome","unknown","short"),
  ("Functional denominator","functional_evaluable","short"),
  ("Infrastructure denominator","infra_evaluable","short"),
  ("Functional failure","functional_failure_ratio","percentunit"),
  ("Functional pass","functional_pass_ratio","percentunit"),
  ("Observed infrastructure failure","infra_failure_ratio","percentunit"),
  ("Duration samples","duration_samples","short"),
  ("Duration p50","duration_p50_seconds","s"),("Duration p95","duration_p95_seconds","s"),
 )),
 ("Independent expected-attempt ledger and freshness",(
  ("Ledger available (1 = yes)","ledger_available","short"),
  ("Mature expected","expected","short"),("Expected covered","covered","short"),
  ("Missing mature terminals","missing","short"),("Unexpected terminals","unexpected","short"),
  ("Observed but still maturing","pending_maturity_observed","short"),
  ("Match coverage","coverage_ratio","percentunit"),("Platform good matched","platform_good","short"),
  ("Platform good / mature expected","platform_good_ratio","percentunit"),
  ("Snapshot start (inclusive UTC)","window_start_timestamp_seconds","dateTimeAsIso"),
  ("Snapshot cutoff (exclusive UTC)","as_of_timestamp_seconds","dateTimeAsIso"),
 )),
)


def variable(name,label,values):
    return {"name":name,"label":label,"type":"custom","query":",".join(values),
            "current":{"value":values[0],"text":values[0]},"includeAll":False,"multi":False,
            "options":[{"text":v,"value":v,"selected":i==0} for i,v in enumerate(values)]}


def generate(teams,environments):
    panels=[{"type":"text","title":"Scope and data limits","id":1,"gridPos":{"x":0,"y":0,"w":24,"h":4},
             "options":{"mode":"markdown","content":
                 "**Observed Runner terminal data only.** Eligibility and expected attempts are independent inventories, "
                 "not inferred from sent events. No data is not zero. A missing terminal is not evidence of inactivity or failure. "
                 "Use the snapshot cutoff to detect stale readouts. Per-operation cards do not infer the internal stages of a run."}}]
    y=4
    for group,stats in GROUPS:
        panels.append({"type":"row","title":group,"id":len(panels)+1,
                       "gridPos":{"x":0,"y":y,"w":24,"h":1},"collapsed":False,"panels":[]})
        y+=1
        for i,(title,metric,unit) in enumerate(stats):
            query='qaas_runner_kpi_'+metric+'{job="qaas_runner_kpi",window="$window",team="$team",environment="$environment"}'
            if unit=="dateTimeAsIso":
                query+=" * 1000"
            panels.append({"type":"stat","title":title,"description":METRICS[metric],"id":len(panels)+1,
                           "datasource":DS,"gridPos":{"x":i%4*6,"y":y+i//4*4,"w":6,"h":4},
                           "targets":[{"expr":query,"instant":True,"range":False,"refId":"A","datasource":DS}],
                           "fieldConfig":{"defaults":{"unit":unit,"noValue":"N/A","color":{"mode":"fixed","fixedColor":"blue"}},"overrides":[]},
                           "options":{"colorMode":"value","graphMode":"none","textMode":"value",
                                      "reduceOptions":{"calcs":["lastNotNull"],"values":False}}})
        y+=((len(stats)+3)//4)*4
    assert {metric for _,stats in GROUPS for _,metric,_ in stats}==set(METRICS)
    return {"uid":"qaas-runner-terminal-kpi","title":"QaaS | Observed Runner usage and coverage",
            "description":"Fixed UTC windows independent of the time picker. Privacy-bounded observed scopes. Not installation estimates.",
            "schemaVersion":41,"version":1,"timezone":"utc","editable":True,"timepicker":{"hidden":True},
            "time":{"from":"now-5m","to":"now"},"tags":["qaas","runner","terminal-kpi"],"panels":panels,
            "templating":{"list":[variable("window","Exact UTC window",["7d","30d"]),
                                  variable("team","Team",teams),variable("environment","Environment",environments)]}}


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--roster",type=Path,required=True)
    p.add_argument("--as-of",required=True)
    p.add_argument("--output",type=Path,required=True)
    a=p.parse_args()
    try:
        if a.roster.resolve() == a.output.resolve():
            raise ValueError("output must differ from roster")
        _, teams, environments = roster(a.roster,tick(a.as_of))
        atomic(a.output,json.dumps(generate(teams,environments),indent=2)+"\n")
    except ValueError as e:
        p.exit(2,f"cannot generate dashboard: {e}\n")
    except OSError:
        p.exit(2,"cannot read roster or publish dashboard\n")


if __name__=="__main__":
    main()

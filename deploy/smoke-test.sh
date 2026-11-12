#!/usr/bin/env bash
# Smoke test after a deploy. The workflow Deploy runs it from outside, over the public address: the shell loads, and a
# ping goes through web host, gateway and Planning into its database and back. The new release may still be warming
# up, so the whole check gets a few tries. Then the walkthrough runs once with the throwaway account the workflow made
# for this run (SMOKE_ACCOUNT, SMOKE_PASSWORD): paste a text, topics, a question, the answer, mastery and today's plan
# moving the topic back. Its documents go to the simulated AI. The workflow deletes the account and all its data
# afterwards. If anything fails after a deploy, the workflow rolls back by itself.
set -euo pipefail

base=${SMOKE_BASE_URL:-https://edvaniq.nullradix.de}
tries=${SMOKE_TRIES:-6}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

check() {
  local status
  status=$(curl -sS -o "$work/page" -w '%{http_code}' --max-time 15 "$base/") || return 1
  [[ $status == 200 ]] || { echo "GET / answered $status"; return 1; }
  grep -q '_framework/blazor.web.js' "$work/page" || { echo "GET / has no Blazor shell"; return 1; }

  status=$(curl -sS -c "$work/cookies" -o /dev/null -w '%{http_code}' --max-time 15 -X POST "$base/session") || return 1
  [[ $status == 204 ]] || { echo "POST /session answered $status"; return 1; }

  status=$(curl -sS -b "$work/cookies" -o "$work/ping" -w '%{http_code}' --max-time 20 -X POST "$base/api/planning/ping") || return 1
  [[ $status == 200 ]] || { echo "POST /api/planning/ping answered $status: $(head -c 300 "$work/ping")"; return 1; }
  python3 - "$work/ping" << 'PY' || return 1
import json
import sys

ping = json.load(open(sys.argv[1]))
if not (isinstance(ping.get("id"), int) and ping["id"] > 0 and isinstance(ping.get("pingedAt"), str)):
    sys.exit(f"The ping came back without the value from the database: {ping}")
print(f"Ping {ping['id']}, stored at {ping['pingedAt']}")
PY
}

# The password only comes from the environment, never as an argument.
walkthrough() {
  python3 - "$base" << 'PY'
import http.cookiejar
import json
import os
import sys
import time
import urllib.error
import urllib.request

base = sys.argv[1]
account = os.environ.get("SMOKE_ACCOUNT", "")
password = os.environ.get("SMOKE_PASSWORD", "")
deadline = time.monotonic() + 180
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
# The text of PlanFlowTests: with the simulated AI every topic of it gets questions.
text = """# Photosynthese
## Lichtreaktion
Licht spaltet Wasser.

## Calvin-Zyklus
CO2 wird zu Zucker.

# Zellatmung
Zucker wird zu Energie.
"""


class Failed(Exception):
    pass


def call(method, path, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(base + path, data=data, method=method)
    if data is not None:
        request.add_header("Content-Type", "application/json")
    try:
        with opener.open(request, timeout=20) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        return error.code, error.read()[:300].decode(errors="replace")


def expect(step, method, path, status, body=None):
    code, answer = call(method, path, body)
    if code != status:
        raise Failed(f"{method} {path} answered {code}, expected {status}: {answer}")
    return answer


def wait(step, probe, interval=2):
    while True:
        result = probe()
        if result is not None:
            return result
        if time.monotonic() > deadline:
            raise Failed("no result within the time limit")
        time.sleep(interval)


def learn_blocks(plan):
    return [block for block in plan["blocks"] if block["kind"] == "learn"]


def minutes_and_place(plan, topic):
    blocks = learn_blocks(plan)
    places = [index for index, block in enumerate(blocks) if block["topicId"] == topic]
    return sum(block["minutes"] for block in blocks if block["topicId"] == topic), places[0] if places else len(blocks)


steps = ["login", "paste", "analysis", "topics", "plan", "question", "answer", "mastery", "replanned"]
step = steps[0]


def go(name):
    global step
    step = name
    print(f"Walkthrough step {steps.index(name) + 1}/{len(steps)}: {name}", flush=True)


try:
    if not account or not password:
        raise Failed("no smoke account, the workflow did not prepare one")

    go("login")
    expect(step, "POST", "/session/login", 204, {"name": account, "password": password})

    go("paste")
    document = expect(step, "POST", "/api/content/documents", 201, {"title": "Durchstich", "text": text})["id"]

    go("analysis")

    def analysed():
        listed = expect(step, "GET", "/api/content/documents", 200)
        mine = next((item for item in listed if item["id"] == document), None)
        if mine is None:
            raise Failed("the document is missing from the list")
        if mine["status"] == "Failed":
            raise Failed(f"the analysis failed: {mine.get('failureReason')}")
        return True if mine["status"] == "Ready" else None

    wait(step, analysed)

    go("topics")

    def topics_of_document():
        tree = expect(step, "GET", "/api/knowledge/topics", 200)
        ids = [topic["id"] for topic in tree if topic.get("documentId") == document]
        ids += [sub["id"] for topic in tree for sub in topic.get("subtopics", []) if sub.get("documentId") == document]
        return ids or None

    topics = wait(step, topics_of_document)

    go("plan")

    def first_plan():
        plan = expect(step, "GET", "/api/planning/plan/today", 200)
        learn = [block for block in learn_blocks(plan) if block["topicId"] in topics]
        return plan if plan.get("masteryAvailable") and learn else None

    before = wait(step, first_plan)
    candidates = list(dict.fromkeys(block["topicId"] for block in learn_blocks(before) if block["topicId"] in topics))[:3]

    go("question")

    # 409 means no questions for the topic yet. One that stays without any for 40 s makes way for the next learn block.
    attempt = None
    for topic in candidates:
        until = min(deadline, time.monotonic() + 40)
        while attempt is None and time.monotonic() < until:
            code, answer = call("POST", "/api/assessment/attempts", {"topicId": topic})
            if code == 201:
                attempt = answer
            elif code == 409:
                time.sleep(2)
            else:
                raise Failed(f"POST /api/assessment/attempts answered {code}: {answer}")
        if attempt is not None:
            break
    if attempt is None:
        plan = expect(step, "GET", "/api/planning/plan/today", 200)
        raise Failed(
            f"no question for any of {len(candidates)} learn block topics of {len(topics)}, "
            f"notReadyTopicIds {plan.get('notReadyTopicIds')}")

    go("answer")
    # The simulated AI puts the right answer first.
    graded = expect(step, "POST", f"/api/assessment/attempts/{attempt['id']}/answer", 200, {"option": 0, "responseTimeMs": 5000})
    if not graded["correct"]:
        raise Failed(f"the first option was not right, answer {graded['answer']}")

    go("mastery")

    def recorded():
        mastery = expect(step, "GET", "/api/learningengine/mastery", 200)
        entry = next((item for item in mastery["topics"] if item["topicId"] == topic), None)
        return entry if entry and entry.get("lastAnswerId") == attempt["id"] else None

    wait(step, recorded, interval=0.5)

    go("replanned")
    minutes, place = minutes_and_place(before, topic)

    def moved_back():
        plan = expect(step, "GET", "/api/planning/plan/today", 200)
        after_minutes, after_place = minutes_and_place(plan, topic)
        return True if after_minutes < minutes or after_place > place else None

    wait(step, moved_back, interval=1)
    print("Walkthrough passed")
except Failed as failure:
    print(f"::error::Walkthrough failed at step {steps.index(step) + 1}/{len(steps)} ({step}): {failure}")
    sys.exit(1)
except Exception as error:
    print(f"::error::Walkthrough failed at step {steps.index(step) + 1}/{len(steps)} ({step}): {type(error).__name__}: {error}")
    sys.exit(1)
PY
}

for try in $(seq 1 "$tries"); do
  if check; then
    echo "Smoke test passed against $base"
    walkthrough
    exit 0
  fi
  if (( try < tries )); then
    sleep 10
  fi
done
echo "::error::Smoke test failed against $base after $tries tries"
exit 1
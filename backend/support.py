"""Versioned support contract for local synthetic tests."""

import re


VERSION = "rescue-support-v1"
DIRECT = "direct_recommendation"
DISSENT = "constructive_dissent"
NO_AI = "no_ai"
REASONS = frozenset({
    "branch_count", "difficulty", "access", "previous_search",
    "days_left", "logistics", "other",
})
SITE_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9 '\-]{0,39}")


class InvalidSupport(ValueError):
    pass


def require(valid):
    if not valid:
        raise InvalidSupport("invalid_support_request")


def fields(value, *names):
    require(type(value) is dict and value.keys() == set(names))


def integer(value, low, high):
    require(type(value) is int and low <= value <= high)


def site_id(value):
    require(type(value) is str and SITE_ID.fullmatch(value) is not None)


def validate_plan(plan, targets, budget, max_expeditions):
    require(type(plan) is list and len(plan) <= max_expeditions)
    used = set()
    total = 0
    for entry in plan:
        fields(entry, "target", "points")
        target = entry["target"]
        site_id(target)
        require(target in targets and target not in used)
        integer(entry["points"], 1, budget)
        used.add(target)
        total += entry["points"]
    require(total <= budget)


def validate_request(request):
    fields(request, "version", "condition", "context")
    require(request["version"] == VERSION)
    require(request["condition"] in (DIRECT, DISSENT, NO_AI))
    context = request["context"]
    fields(context, "day", "total_days", "points_available", "daily_cap",
           "max_expeditions", "sites", "provisional_plan", "initial_judgments")
    integer(context["total_days"], 1, 14)
    integer(context["day"], 1, context["total_days"])
    integer(context["points_available"], 0, 100)
    integer(context["daily_cap"], 1, 100)
    integer(context["max_expeditions"], 1, 12)
    budget = min(context["points_available"], context["daily_cap"])

    sites = context["sites"]
    require(type(sites) is list and 1 <= len(sites) <= 64)
    parents = {}
    for site in sites:
        fields(site, "id", "parent", "known_difficulty", "reported_branch_remaining",
               "known_remaining_here", "known_remaining_beyond")
        node, parent = site["id"], site["parent"]
        site_id(node)
        site_id(parent)
        require(node != "Camp" and node not in parents)
        parents[node] = parent
        for name in ("known_difficulty", "reported_branch_remaining",
                     "known_remaining_here", "known_remaining_beyond"):
            if site[name] is not None:
                integer(site[name], 0, 100)
    for target in parents:
        seen = set()
        current = target
        while current != "Camp":
            require(current in parents and current not in seen)
            seen.add(current)
            current = parents[current]

    max_expeditions = context["max_expeditions"]
    validate_plan(context["provisional_plan"], parents, budget, max_expeditions)
    judgments = context["initial_judgments"]
    require(type(judgments) is list and len(judgments) == 3)
    for judgment in judgments:
        fields(judgment, "branch", "reason", "plan")
        if judgment["branch"] is not None:
            site_id(judgment["branch"])
            require(parents.get(judgment["branch"]) == "Camp")
        if judgment["reason"] is not None:
            require(type(judgment["reason"]) is str and judgment["reason"] in REASONS)
        validate_plan(judgment["plan"], parents, budget, max_expeditions)


def build_mock_response(request):
    validate_request(request)
    condition, context = request["condition"], request["context"]
    plan = None
    if condition == NO_AI:
        provider = "none"
        message = "Continue discussing the rescue plan. No additional information is provided."
    elif condition == DIRECT:
        provider = "mock"
        plan = context["provisional_plan"]
        options = ", ".join(f"{leg['points']} LP to {leg['target']}" for leg in plan)
        if plan:
            message = f"Test recommendation: send {options}. Unknown thresholds could still stop an expedition."
        else:
            message = "Test recommendation: launch no expeditions and retain today's unspent points."
    else:
        provider = "mock"
        message = "Test discussion prompt: what assumption about route difficulty or survivor distribution would change your plan?"
    return {"version": VERSION, "condition": condition, "provider": provider,
            "message": message, "recommended_plan": plan}

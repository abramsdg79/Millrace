"""Writes the FUXA project hmi/fuxa/mine-conveyors.fuxap.json, its source (plan 8).

From the repository root:
    dotnet run --project src/Dse.Cli -- modbus-map samples/mine-conveyors/plant.json --format fuxa --out <tags.json>
    python3 -I hmi/fuxa/generate-project.py <tags.json> hmi/fuxa/mine-conveyors.fuxap.json
The output is a pure function of the tags: same tags, same bytes. Change the
views, alarms, charts or script here, not in the JSON: regenerating replaces it.
"""
import json
import sys
from xml.sax.saxutils import escape

tags = json.load(open(sys.argv[1]))
out = sys.argv[2]

DEV = "dse"


def t(name):
    tid = "t_" + name
    assert tid in tags, tid
    return tid


# ---------------------------------------------------------------- svg helpers
class View:
    def __init__(self, vid, name, width, height):
        self.vid, self.name, self.w, self.h = vid, name, width, height
        self.svg = []
        self.items = {}

    def raw(self, s):
        self.svg.append(s)

    def text(self, eid, x, y, s, size=14, fill="#263238", anchor="start", weight="normal"):
        self.raw(f'<text id="{eid}" x="{x}" y="{y}" font-family="sans-serif" font-size="{size}" '
                 f'font-weight="{weight}" fill="{fill}" stroke-width="0" text-anchor="{anchor}" '
                 f'xml:space="preserve">{escape(s)}</text>')

    def value(self, key, x, y, tag, unit, digits, size=14, anchor="start", label=None):
        gid = f"VAL_{key}"
        self.raw(f'<g id="{gid}" type="svg-ext-value" fill="#0d47a1" stroke="#0d47a1" font-size="{size}" '
                 f'stroke-width="0" font-family="sans-serif" text-anchor="{anchor}">'
                 f'<text id="VAL_{key}_t" x="{x}" y="{y}" fill="#0d47a1" stroke="#0d47a1" stroke-width="0" '
                 f'font-size="{size}" font-family="sans-serif" text-anchor="{anchor}" xml:space="preserve">##.##</text></g>')
        rng = {"type": 1, "min": 0, "max": 0, "color": "", "stroke": "", "text": unit}
        if digits is not None:
            rng["fractionDigits"] = digits
        self.items[gid] = {"id": gid, "type": "svg-ext-value", "name": label or key,
                           "property": {"events": [], "actions": [], "variableSrc": DEV, "variableId": tag,
                                        "variable": tag, "ranges": [rng]},
                           "label": "Value", "hide": False, "lock": False}

    def shape(self, key, element, tag, ranges, actions=None, label=None):
        """element: an svg element string with id=key."""
        self.raw(element)
        prop = {"events": [], "actions": actions or [], "variableSrc": DEV, "variableId": tag, "variable": tag,
                "ranges": [{"type": 2, "min": lo, "max": hi, "color": c, "stroke": s} for (lo, hi, c, s) in ranges]}
        self.items[key] = {"id": key, "type": "svg-ext-shapes-" + element[1:element.index(" ")], "name": label or key,
                           "property": prop, "label": "Shapes", "hide": False, "lock": False}

    def lamp(self, key, cx, cy, tag, on, off, caption):
        self.shape(key, f'<circle id="{key}" cx="{cx}" cy="{cy}" r="7" fill="{off}" stroke="#37474f" stroke-width="1"/>',
                   tag, [(0, 0, off, ""), (1, 1, on, "")], label=caption)
        self.text(key + "_l", cx + 12, cy + 5, caption, size=12)

    def button(self, key, x, y, w, h, text, events, ranges=None, tag=None, bk="#1565c0", fg="#ffffff"):
        gid = f"HXB_{key}"
        style = (f"width:calc(100% - 6px);height:calc(100% - 6px);text-align:center;background-color:{bk};"
                 f"color:{fg};font-size:13px;font-family:sans-serif;")
        self.raw(f'<g id="{gid}" type="svg-ext-html_button" fill="rgba(0,0,0,0)" '
                 f'stroke="rgba(0,0,0,0)" xml:space="preserve">'
                 f'<rect id="{gid}_r" x="{x}" y="{y}" width="{w}" height="{h}" stroke-width="0"/>'
                 f'<foreignObject id="H-{gid}" x="{x}" y="{y}" width="{w}" height="{h}">'
                 f'<button xmlns="http://www.w3.org/1999/xhtml" id="B-{gid}" class="md-btn  md-btn-raised" style="{style}">{escape(text)}</button>'
                 f'</foreignObject></g>')
        prop = {"events": events, "actions": [], "text": text,
                "ranges": [{"type": 2, "min": lo, "max": hi, "color": c, "stroke": s} for (lo, hi, c, s) in (ranges or [])]}
        if tag:
            prop.update({"variableSrc": DEV, "variableId": tag, "variable": tag})
        self.items[gid] = {"id": gid, "type": "svg-ext-html_button", "name": text, "property": prop,
                           "label": "HtmlButton", "hide": False, "lock": False}

    def control(self, key, ctype, x, y, w, h, prop, label, name):
        prefix = "HXC_" if ctype == "svg-ext-html_chart" else "OXC_"
        gid = f"{prefix}{key}"
        self.raw(f'<g id="{gid}" type="{ctype}" fill="#ffffff" stroke="#000000" '
                 f'stroke-width="1" font-size="14" font-family="sans-serif" text-anchor="right" xml:space="preserve">'
                 f'<rect id="{gid}_r" x="{x}" y="{y}" width="{w}" height="{h}" stroke-width="0" fill="none"/>'
                 f'<foreignObject id="H-{gid}" x="{x}" y="{y}" width="{w}" height="{h}">'
                 f'<div xmlns="http://www.w3.org/1999/xhtml" id="D-{gid}" style="width:100%;height:100%;"></div>'
                 f'</foreignObject></g>')
        self.items[gid] = {"id": gid, "type": ctype, "name": name, "property": prop, "label": label,
                           "hide": False, "lock": False}

    def to_json(self):
        svg = (f'<svg width="{self.w}" height="{self.h}" xmlns="http://www.w3.org/2000/svg" '
               f'xmlns:svg="http://www.w3.org/2000/svg"><g><title>Layer 1</title>' + "".join(self.svg) + "</g></svg>")
        return {"id": self.vid, "name": self.name, "type": "svg",
                "profile": {"width": self.w, "height": self.h, "bkcolor": "#eceff1ff", "margin": 10,
                            "align": "topCenter", "gridType": "fixed", "viewRenderDelay": 0},
                "items": self.items, "variables": {}, "svgcontent": svg, "property": {"events": []}}


PULSE = "s_pulse"


def pulse(tag_list):
    return [{"type": "click", "action": "onRunScript", "actparam": PULSE,
             "actoptions": {"params": [{"name": "tags", "type": "value", "value": ",".join(tag_list)}]}}]


def toggle(tag):
    return [{"type": "click", "action": "onToggleValue", "actparam": "",
             "actoptions": {"variable": {"variableId": tag}}}]


GREY, GREEN, RED, AMBER, DARK = "#9e9e9e", "#2e7d32", "#c62828", "#f9a825", "#37474f"

# ---------------------------------------------------------------- Overview
ov = View("v_overview", "Overview", 1280, 720)
ov.text("ov_title", 24, 40, "Mine conveyors — overview", size=24, weight="bold")
ov.text("ov_sub", 24, 64, "Simulated by DSE over Modbus TCP · belt green running, grey stopped, red tripped", size=13,
        fill="#546e7a")

# Ore source (feeder hopper)
ov.raw('<polygon id="ov_hopper" points="30,100 130,100 110,150 50,150" fill="#8d6e63" stroke="#4e342e" stroke-width="2"/>')
ov.text("ov_hopper_l", 30, 92, "Ore feed", size=14, weight="bold")
ov.lamp("SHE_feed_enabled", 40, 172, t("Feed.Enabled"), GREEN, GREY, "Enabled")
ov.lamp("SHE_feed_ok", 40, 194, t("INT_FEED.Ok"), GREEN, RED, "INT_FEED Ok")
ov.value("feed_mass", 40, 222, t("Feed.HopperMass"), "kg in hopper", 1, size=13)

belts = [
    ("CV001", 140, 160, 380, "CH1", 510),
    ("CV002", 500, 290, 360, "CH2", 850),
    ("CV003", 840, 420, 300, None, None),
]
for (cv, x, y, w, chute, cx) in belts:
    k = cv.lower()
    ov.text(f"ov_{k}_name", x + 60, y - 12, cv, size=16, weight="bold")
    # running/stopped colour by the contactor's auxiliary contact
    ov.shape(f"SHE_{k}_belt",
             f'<rect id="SHE_{k}_belt" x="{x}" y="{y}" width="{w}" height="22" rx="11" ry="11" fill="{GREY}" '
             f'stroke="{DARK}" stroke-width="2"/>',
             t(f"{cv}.Contactor"), [(0, 0, GREY, ""), (1, 1, GREEN, "")], label=f"{cv} belt")
    # tripped overlay: shown while the belt's interlock is latched
    ov.shape(f"SHE_{k}_trip",
             f'<rect id="SHE_{k}_trip" x="{x}" y="{y}" width="{w}" height="22" rx="11" ry="11" fill="{RED}" '
             f'stroke="{DARK}" stroke-width="2"/>',
             t(f"INT_{cv}.Tripped"), [],
             actions=[{"variableId": t(f"INT_{cv}.Tripped"), "variableSrc": DEV, "variable": t(f"INT_{cv}.Tripped"),
                       "bitmask": 0, "range": {"min": 0, "max": 0}, "type": "hide", "options": {}},
                      {"variableId": t(f"INT_{cv}.Tripped"), "variableSrc": DEV, "variable": t(f"INT_{cv}.Tripped"),
                       "bitmask": 0, "range": {"min": 1, "max": 1}, "type": "show", "options": {}}],
             label=f"{cv} tripped")
    ov.raw(f'<circle id="ov_{k}_tail" cx="{x + 11}" cy="{y + 11}" r="5" fill="#eceff1" stroke="{DARK}"/>')
    ov.raw(f'<circle id="ov_{k}_head" cx="{x + w - 11}" cy="{y + 11}" r="5" fill="#eceff1" stroke="{DARK}"/>')
    # values
    ov.value(f"{k}_speed", x, y + 44, t(f"{cv}.Speed"), "m/s", 2, label=f"{cv} speed")
    ov.value(f"{k}_load", x + 110, y + 44, t(f"{cv}.TonnesPerHour"), "t/h", 1, label=f"{cv} load")
    ov.value(f"{k}_current", x + 220, y + 44, t(f"{cv}.Current"), "A", 1, label=f"{cv} current")
    # lamps
    ov.lamp(f"SHE_{k}_contactor", x + 6, y + 64, t(f"{cv}.Contactor"), GREEN, GREY, "Contactor")
    ov.lamp(f"SHE_{k}_safety", x + 106, y + 64, t(f"{cv}.SafetyOk"), GREEN, RED, "Safety relay")
    ov.lamp(f"SHE_{k}_zero", x + 216, y + 64, t(f"{cv}.Stopped"), AMBER, GREY, "Zero speed")
    ov.lamp(f"SHE_{k}_intok", x + 6, y + 86, t(f"INT_{cv}.Ok"), GREEN, RED, f"INT_{cv} Ok")
    if chute:
        ov.raw(f'<polygon id="ov_{chute.lower()}" points="{cx - 20},{y + 4} {cx + 30},{y + 4} {cx + 14},{y + 120} {cx - 4},{y + 120}" '
               f'fill="#b0bec5" stroke="{DARK}" stroke-width="2"/>')
        ov.text(f"ov_{chute.lower()}_l", cx + 36, y + 40, chute, size=13, weight="bold")

# stockpile
ov.raw(f'<polygon id="ov_pile" points="1120,560 1250,560 1185,470" fill="#8d6e63" stroke="#4e342e" stroke-width="2"/>')
ov.text("ov_pile_l", 1120, 584, "Stockpile", size=14, weight="bold")
ov.value("pile_rate", 1120, 604, t("Stockpile.Rate"), "kg/s", 1, size=13, label="Stockpile rate")

# operator panel
px, py = 24, 470
ov.raw(f'<rect id="ov_panel" x="{px - 8}" y="{py - 30}" width="760" height="232" rx="6" fill="#ffffff" stroke="#b0bec5"/>')
ov.text("ov_panel_l", px, py - 10, "Operator", size=15, weight="bold")
ov.button("start", px, py, 170, 40, "Start line",
          pulse([t("SEQ_START.Reset"), t("SEQ_START.Start")]), bk=GREEN)
ov.button("stop", px + 180, py, 170, 40, "Stop line", pulse([t("SEQ_STOP.Reset"), t("SEQ_STOP.Start")]), bk="#ef6c00")
ov.button("safety_reset", px + 360, py, 170, 40, "Reset safety relays",
          pulse([t("CV001.SafetyReset"), t("CV002.SafetyReset"), t("CV003.SafetyReset")]))
ov.button("int_reset", px + 540, py, 190, 40, "Reset interlocks",
          pulse([t("INT_CV003.Reset"), t("INT_CV002.Reset"), t("INT_CV001.Reset"), t("INT_FEED.Reset")]))
ov.value("seq_start_step", px, py + 64, t("SEQ_START.Step"), "", None, size=13, label="SEQ_START step")
ov.text("ov_seq_start_l", px + 20, py + 64, "SEQ_START step", size=13)
ov.value("seq_stop_step", px + 180, py + 64, t("SEQ_STOP.Step"), "", None, size=13, label="SEQ_STOP step")
ov.text("ov_seq_stop_l", px + 200, py + 64, "SEQ_STOP step", size=13)
ov.lamp("SHE_seq_complete", px + 366, py + 60, t("SEQ_START.Complete"), GREEN, GREY, "Start complete")
ov.lamp("SHE_seq_faulted", px + 546, py + 60, t("SEQ_START.Faulted"), RED, GREY, "Start faulted")
for i, cv in enumerate(["CV001", "CV002", "CV003"]):
    k = cv.lower()
    bx = px + i * 245
    ov.button(f"{k}_pullkey", bx, py + 90, 115, 36, f"{cv} pull-key", toggle(t(f"{cv}.PullKey1")),
              ranges=[(0, 0, "#546e7a", "#ffffff"), (1, 1, RED, "#ffffff")], tag=t(f"{cv}.PullKey1"), bk="#546e7a")
    ov.button(f"{k}_estop", bx + 120, py + 90, 115, 36, f"{cv} e-stop", toggle(t(f"{cv}.EStop")),
              ranges=[(0, 0, "#546e7a", "#ffffff"), (1, 1, RED, "#ffffff")], tag=t(f"{cv}.EStop"), bk="#546e7a")
ov.text("ov_toggle_note", px, py + 150, "Pull-key and e-stop buttons latch: red is actuated; press again to restore,",
        size=12, fill="#546e7a")
ov.text("ov_toggle_note2", px, py + 166, "then reset the safety relays and the interlocks, and start the line.", size=12,
        fill="#546e7a")

# ---------------------------------------------------------------- Alarms
al = View("v_alarms", "Alarms", 1280, 720)
al.text("al_title", 24, 40, "Alarms", size=24, weight="bold")
al.text("al_sub", 24, 64, "Current alarms: motor current Hi/HiHi (the sample's ALM_ blocks) and interlock trips", size=13,
        fill="#546e7a")
cols = [("ontime", "Time"), ("text", "Alarm"), ("type", "Priority"), ("group", "Group"), ("status", "Status"),
        ("ack", "Ack")]
widths = {"ontime": 170, "text": 520, "type": 110, "group": 140, "status": 150, "ack": 80}
table = {"id": None, "type": "alarms", "events": [], "options": {
    "paginator": {"show": False}, "filter": {"show": False}, "daterange": {"show": False}, "realtime": False,
    "lastRange": "last1h", "gridColor": "#E0E0E0",
    "header": {"show": True, "height": 32, "fontSize": 13, "background": "#ECEFF1", "color": "#37474F"},
    "row": {"height": 30, "fontSize": 13, "background": "#FFFFFF", "color": "#000000"},
    "selection": {"background": "#3059AF", "color": "#FFFFFF", "fontBold": True},
    "columns": [], "alarmsColumns": [{"id": c, "type": "label", "label": l, "align": "left", "width": widths[c]}
                                     for c, l in cols],
    "alarmFilter": {"filterA": [], "filterB": [], "filterC": []}, "reportsColumns": [],
    "reportFilter": {"filterA": []}, "rows": []}}
al.control("alarms", "svg-ext-own_ctrl-table", 24, 90, 1232, 600, table, "HtmlTable", "Current alarms")

alarms = []
for cv in ["CV001", "CV002", "CV003"]:
    for lvl, key in [("Hi", "high"), ("HiHi", "highhigh")]:
        a = {"name": f"ALM_{cv}.{lvl}", "property": {"variableId": t(f"ALM_{cv}.{lvl}.Active"), "permission": None},
             "highhigh": {"enabled": False}, "high": {"enabled": False}, "low": {"enabled": False},
             "info": {"enabled": False}, "actions": {"enabled": False, "values": []}}
        a[key] = {"enabled": True, "checkdelay": 1, "min": 1, "max": 1, "timedelay": 0,
                  "text": f"{cv} motor current {lvl}", "group": "Current", "ackmode": "ackactive",
                  "bkcolor": "#ffcdd2" if key == "highhigh" else "#ffe0b2", "color": "#000000"}
        alarms.append(a)
for intl in ["INT_CV001", "INT_CV002", "INT_CV003", "INT_FEED"]:
    a = {"name": f"{intl}.Tripped", "property": {"variableId": t(f"{intl}.Tripped"), "permission": None},
         "highhigh": {"enabled": False}, "high": {"enabled": True, "checkdelay": 1, "min": 1, "max": 1, "timedelay": 0,
                                                  "text": f"{intl} tripped", "group": "Interlock",
                                                  "ackmode": "ackactive", "bkcolor": "#ffe0b2", "color": "#000000"},
         "low": {"enabled": False}, "info": {"enabled": False}, "actions": {"enabled": False, "values": []}}
    alarms.append(a)

# ---------------------------------------------------------------- Trends
tr = View("v_trends", "Trends", 1280, 720)
tr.text("tr_title", 24, 40, "Trends", size=24, weight="bold")
tr.text("tr_sub", 24, 64, "Live since this view opened — ten minutes at most", size=13, fill="#546e7a")
colors = {"CV001": "#1565c0", "CV002": "#2e7d32", "CV003": "#ef6c00"}
charts = []
for i, (cid, title, field, unit) in enumerate([("c_speed", "Belt speed (m/s)", "Speed", "m/s"),
                                                ("c_current", "Motor current (A)", "Current", "A")]):
    charts.append({"id": cid, "name": title, "lines": [
        {"device": "DSE", "id": t(f"{cv}.{field}"), "name": f"{cv}.{field}", "label": f"{cv} {unit}",
         "color": colors[cv], "yaxis": 1, "lineInterpolation": 0, "lineWidth": 2, "spanGaps": True}
        for cv in ["CV001", "CV002", "CV003"]]})
    opts = {"title": title, "fontFamily": "sans-serif", "legendFontSize": 12, "colorBackground": "rgba(255,255,255,1)",
            "legendBackground": "rgba(255,255,255,0)", "titleHeight": 20, "axisLabelFontSize": 12,
            "labelsDivWidth": 0, "axisLineColor": "rgba(0,0,0,1)", "axisLabelColor": "rgba(0,0,0,1)",
            "legendMode": "always", "series": [], "width": 1232, "height": 290, "decimalsPrecision": 2,
            "realtime": 10, "dateFormat": "YYYY_MM_DD", "timeFormat": "hh_mm_ss", "lastRange": "last8h",
            "gridLineColor": "rgba(0,0,0,0.2)"}
    tr.control(cid, "svg-ext-html_chart", 24, 90 + i * 310, 1232, 290,
               {"id": cid, "type": "realtime1", "options": opts, "events": []}, "HtmlChart", title)

# ---------------------------------------------------------------- project
device = {"id": DEV, "name": "DSE", "type": "ModbusTCP", "enabled": True, "polling": 200,
          "property": {"address": "dse:5020", "port": None, "slot": None, "rack": None, "slaveid": "1",
                       "baudrate": None, "databits": None, "stopbits": None, "parity": None,
                       "connectionOption": "TcpPort", "delay": 10, "socketReuse": None, "forceFC16": False},
          "tags": tags}
script_code = ("const hold = ms => new Promise(resolve => setTimeout(resolve, ms));\n"
               "for (const tag of tags.split(',')) {\n"
               "    await $setTag(tag, true);\n"
               "    await hold(500);\n"
               "    await $setTag(tag, false);\n"
               "}")
project = {
    "version": "1.01",
    "name": "DSE mine conveyors",
    "server": {"id": "0", "name": "FUXA Server", "type": "FuxaServer", "property": {}, "enabled": True, "tags": {}},
    "devices": {DEV: device},
    "hmi": {"views": [ov.to_json(), al.to_json(), tr.to_json()],
            "layout": {"autoresize": False, "start": "v_overview", "showdev": True, "inputdialog": "false",
                       "hidenavigation": False, "theme": "", "zoom": "enabled",
                       "navigation": {"mode": "fix", "type": "block", "bkcolor": "#263238", "fgcolor": "#ffffff",
                                      "items": [{"text": "Overview", "view": "v_overview", "icon": "home", "link": ""},
                                                {"text": "Alarms", "view": "v_alarms", "icon": "notifications",
                                                 "link": ""},
                                                {"text": "Trends", "view": "v_trends", "icon": "show_chart",
                                                 "link": ""}]},
                       "header": {"title": "DSE mine conveyors", "alarms": "fix", "infos": "", "bkcolor": "#ffffff",
                                  "fgcolor": "#000000", "height": 46, "buttonHeight": 36, "fontSize": 13,
                                  "items": [], "itemsAnchor": "left"}}},
    "charts": charts, "graphs": [], "alarms": alarms, "notifications": [],
    "scripts": [{"id": PULSE, "name": "pulse", "code": script_code, "sync": False, "mode": "SERVER",
                 "parameters": [{"name": "tags", "type": "value"}], "permission": None}],
    "reports": [], "texts": [], "recipes": [],
}
json.dump(project, open(out, "w"), indent=2, ensure_ascii=False)
open(out, "a").write("\n")
print("views", [len(v["items"]) for v in project["hmi"]["views"]])

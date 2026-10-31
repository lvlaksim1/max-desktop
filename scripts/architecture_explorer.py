#!/usr/bin/env python3
"""Visualize .NET project references without changing program source.

Version 1 is a project-level architecture graph. It is NOT a semantic Roslyn
class/method dependency analyzer; unknown conditional MSBuild imports are not inferred.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import html
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

IGNORED = {".git", ".tools", "bin", "obj", "node_modules", "publish", "dist", "quality"}
SVG_TITLE = "Архитектура проекта — карта зависимостей"


def project_files(root):
    return sorted(
        p for p in root.rglob("*.csproj")
        if not any(x in IGNORED for x in p.relative_to(root).parts)
    )


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


def graph(root):
    root = root.resolve()
    paths = project_files(root)
    projects = {path.resolve(): path.relative_to(root).as_posix() for path in paths}
    nodes, edges, errors = [], [], []
    for path in paths:
        key = projects[path.resolve()]
        tree = ET.parse(path)
        refs, packages = [], []
        for element in tree.iter():
            name = local_name(element.tag)
            if name == "ProjectReference":
                include = element.attrib.get("Include", "")
                target = (path.parent / include.replace("\\", "/")).resolve()
                if target not in projects:
                    errors.append("Unresolved reference: " + key + " -> " + include)
                else:
                    dest = projects[target]
                    edges.append({"from": key, "to": dest})
                    refs.append(dest)
            elif name == "PackageReference":
                pkg = element.attrib.get("Include") or element.attrib.get("Update")
                if pkg:
                    packages.append(pkg)
        nodes.append({"id": key, "name": path.stem, "references": sorted(set(refs)),
                      "packages": sorted(set(packages))})
    adjacent = {node["id"]: node["references"] for node in nodes}
    seen, visiting, cycles = set(), set(), []
    def dfs(node, stack):
        if node in visiting:
            cycles.append(stack[stack.index(node):] + [node])
            return
        if node in seen:
            return
        visiting.add(node)
        for target in adjacent[node]:
            dfs(target, stack + [target])
        visiting.remove(node)
        seen.add(node)
    for node in adjacent:
        dfs(node, [node])
    return {"schema": "dotnet-architecture-project-graph-v1",
            "generated_utc": datetime.now(timezone.utc).isoformat(),
            "project_count": len(nodes), "reference_count": len(edges),
            "nodes": nodes, "edges": edges, "unresolved": errors, "cycles": cycles,
            "limitations": "Static .csproj ProjectReference graph only; MSBuild conditions not evaluated."}


def render_html(report):
    payload = json.dumps(report, ensure_ascii=False).replace("<", "\\u003c")
    return r"""<!doctype html>
<html lang="ru"><head><meta charset="utf-8">
<title>Архитектура .NET — проектные зависимости</title>
<style>
:root {color-scheme:dark} body {font:14px system-ui,Arial,sans-serif;background:#101820;color:#edf2f5;margin:0}
header {padding:18px 26px;background:#172734;display:flex;align-items:center;gap:24px;flex-wrap:wrap}
h1{font-size:20px;margin:0}input{padding:10px;color:#fff;background:#243746;border:1px solid #62717a;border-radius:6px}
main{display:grid;grid-template-columns:minmax(400px,1fr) 330px;height:calc(100vh - 88px)}
#scene{width:100%;height:100%;overflow:auto}#detail{border-left:1px solid #374957;padding:20px;overflow:auto}
.node{cursor:pointer}.node rect{fill:#214457;stroke:#4b94ad;stroke-width:1.5}
.node.selected rect{stroke:#eaca86;stroke-width:3}.node text{fill:#fff;font-size:13px}
.edge{stroke:#8ba6b4;stroke-width:1.7;fill:none;marker-end:url(#arrow)}
code{color:#d1b880;overflow-wrap:anywhere} li{margin:7px 0}.muted{color:#a5b6bf}
</style></head><body><header><h1>Карта архитектуры .NET</h1>
<input id="find" aria-label="Поиск проекта" placeholder="Поиск по названию проекта">
<span id="stats"></span></header>
<main><svg id="scene" role="img" aria-label="Граф ссылок ProjectReference"></svg>
<aside id="detail"><p>Выберите компонент на схеме.</p></aside></main>
<script>
const data = __DATA__;
const svg = document.getElementById("scene"), detail=document.getElementById("detail"),
search=document.getElementById("find");
const NS="http://www.w3.org/2000/svg", positions={};let selected=null;
function create(tag,attrs={},parent=svg){
  const el=document.createElementNS(NS,tag);
  for(const [k,v] of Object.entries(attrs))el.setAttribute(k,String(v));
  parent.appendChild(el);return el;
}
function draw(){
  svg.textContent="";Object.keys(positions).forEach(k=>delete positions[k]);
  const definitions=create("defs"), marker=create("marker",{
    id:"arrow",viewBox:"0 0 10 10",refX:9,refY:5,markerWidth:6,markerHeight:6,orient:"auto"},definitions);
  create("path",{d:"M0,0 L10,5 L0,10 Z",fill:"#8ba6b4"},marker);
  const filtered=data.nodes.filter(n=>n.name.toLowerCase().includes(search.value.toLowerCase()));
  const w=250,h=78,dx=300,dy=132,colCount=3;
  filtered.forEach((n,i)=>{positions[n.id]={x:40+(i%colCount)*dx,y:40+Math.floor(i/colCount)*dy}});
  const rows=Math.ceil(filtered.length/colCount);
  svg.setAttribute("viewBox","0 0 "+(colCount*dx+70)+" "+Math.max(320,rows*dy+70));
  data.edges.forEach(e=>{
    const a=positions[e.from],b=positions[e.to];if(!a||!b)return;
    const x1=a.x+w/2,y1=a.y+h,x2=b.x+w/2,y2=b.y;
    const d="M"+x1+" "+y1+" Q"+((x1+x2)/2)+" "+((y1+y2)/2)+" "+x2+" "+y2;
    create("path",{d,class:"edge"});
  });
  filtered.forEach(n=>{
    const p=positions[n.id];
    const group=create("g",{class:"node"+(selected===n.id?" selected":"")});
    create("rect",{x:p.x,y:p.y,rx:10,width:w,height:h},group);
    const label=create("text",{x:p.x+12,y:p.y+31},group);
    label.textContent=n.name.length>31?n.name.substring(0,29)+"…":n.name;
    const sub=create("text",{x:p.x+12,y:p.y+55},group);
    sub.setAttribute("fill","#aac1cb");sub.textContent="Ссылок: "+n.references.length;
    group.addEventListener("click",()=>select(n.id));
  });
  document.getElementById("stats").textContent=
    data.project_count+" проектов, "+data.reference_count+" связей, "+data.unresolved.length+" ошибок ссылок";
}
function select(id){
  selected=id;const n=data.nodes.find(x=>x.id===id);if(!n)return;
  detail.textContent="";
  function section(tag,value){
    const el=document.createElement(tag);el.textContent=value;detail.appendChild(el);return el;
  }
  section("h2",n.name);
  const source=section("code",n.id);source.setAttribute("title","Путь в рабочем проекте");
  section("h3","Зависимости");
  if(!n.references.length)section("p","Нет ссылок на другие проекты.");
  else {
    const ul=section("ul","");
    n.references.forEach(x=>{const li=document.createElement("li");li.textContent=x;ul.appendChild(li);});
  }
  section("h3","Зависимые проекты");
  const parents=data.edges.filter(e=>e.to===id).map(e=>e.from);
  if(!parents.length)section("p","Нет входящих ссылок.");
  else{
    const ul=section("ul","");
    parents.forEach(x=>{const li=document.createElement("li");li.textContent=x;ul.appendChild(li);});
  }
  section("h3","Пакеты NuGet");
  section("p",n.packages.join(", ")||"Не указаны");
  draw();
}
search.addEventListener("input",draw);draw();
</script></body></html>""".replace("__DATA__", payload)


def main():
    parser = argparse.ArgumentParser(description=SVG_TITLE)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--check", action="store_true", help="Fail on missing references or cycles")
    args = parser.parse_args()
    root = args.root.resolve()
    if not root.is_dir():
        print("ARCHITECTURE_ROOT_MISSING", file=sys.stderr)
        return 2
    try:
        report = graph(root)
    except (OSError, ET.ParseError) as exc:
        print("ARCHITECTURE_PARSE_ERROR: " + str(exc), file=sys.stderr)
        return 2
    out = root / "quality" / "reports"
    out.mkdir(parents=True, exist_ok=True)
    (out / "architecture.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    (out / "architecture.html").write_text(render_html(report), encoding="utf-8")
    print("ARCHITECTURE_PROJECTS=" + str(report["project_count"]))
    print("ARCHITECTURE_EDGES=" + str(report["reference_count"]))
    print("UNRESOLVED=" + str(len(report["unresolved"])))
    print("CYCLES=" + str(len(report["cycles"])))
    print("HTML=" + str(out / "architecture.html"))
    if args.check and (not report["project_count"] or report["unresolved"] or report["cycles"]):
        for line in report["unresolved"][:12]:
            print("ERROR: " + line, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

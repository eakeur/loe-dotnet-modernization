using System.Text;
using System.Text.Json;

namespace DepTree;

public static class HtmlReportGenerator
{
    public static string Generate(SolutionOutput data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
            WriteIndented               = false,
            DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        var sb = new StringBuilder();
        sb.Append(HtmlTemplate.Replace("/*INJECT_DATA*/", $"const DATA = {json};"));
        return sb.ToString();
    }

    // The HTML template is a plain string constant — no PowerShell heredoc,
    // no escaping nightmares. JS template literals live here safely.
    private const string HtmlTemplate = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="UTF-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <title>.NET Migration — Dependency Tree</title>
        <style>
          :root {
            --bg:#0d1117;--bg2:#161b22;--bg3:#21262d;--border:#30363d;
            --text:#e6edf3;--muted:#8b949e;--accent:#58a6ff;--accent2:#3fb950;
            --warn:#d29922;--danger:#f85149;--tag-bg:#1f3040;
            --mono:'JetBrains Mono','Fira Code','Cascadia Code',monospace;
            --sans:'Inter','Segoe UI',system-ui,sans-serif;
            --radius:6px;--transition:160ms ease;
          }
          *,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
          html,body{height:100%;background:var(--bg);color:var(--text);font-family:var(--sans);font-size:14px;line-height:1.6;overflow:hidden}
          .app{display:grid;grid-template-rows:56px 1fr;grid-template-columns:280px 1fr 380px;grid-template-areas:"header header header" "sidebar graph panel";height:100vh}
          header{grid-area:header;background:var(--bg2);border-bottom:1px solid var(--border);display:flex;align-items:center;gap:20px;padding:0 20px}
          .logo{font-family:var(--mono);font-size:13px;font-weight:700;color:var(--accent);letter-spacing:.04em;white-space:nowrap}
          .logo span{color:var(--muted);font-weight:400}
          .summary-pills{display:flex;gap:8px;flex-wrap:wrap}
          .pill{display:flex;align-items:center;gap:5px;background:var(--bg3);border:1px solid var(--border);border-radius:20px;padding:2px 10px;font-size:12px;font-family:var(--mono);white-space:nowrap}
          .pill .dot{width:7px;height:7px;border-radius:50%;flex-shrink:0}
          .dot-blue{background:var(--accent)}.dot-green{background:var(--accent2)}.dot-warn{background:var(--warn)}.dot-danger{background:var(--danger)}.dot-muted{background:var(--muted)}
          .header-right{margin-left:auto;display:flex;gap:8px;align-items:center}
          .search-wrap{position:relative}
          .search-wrap input{background:var(--bg3);border:1px solid var(--border);border-radius:var(--radius);color:var(--text);font-family:var(--sans);font-size:13px;padding:5px 10px 5px 30px;width:220px;outline:none;transition:border-color var(--transition)}
          .search-wrap input:focus{border-color:var(--accent)}
          .search-wrap .si{position:absolute;left:9px;top:50%;transform:translateY(-50%);color:var(--muted);font-size:13px;pointer-events:none}
          .sidebar{grid-area:sidebar;background:var(--bg2);border-right:1px solid var(--border);display:flex;flex-direction:column;overflow:hidden}
          .sidebar-header{padding:12px 14px 8px;border-bottom:1px solid var(--border);display:flex;align-items:center;justify-content:space-between}
          .sidebar-header h3{font-size:11px;text-transform:uppercase;letter-spacing:.08em;color:var(--muted);font-weight:600}
          .filter-tabs{display:flex;gap:2px;padding:6px 10px;border-bottom:1px solid var(--border)}
          .ftab{flex:1;padding:4px 0;font-size:11px;font-family:var(--mono);border-radius:4px;border:none;background:transparent;color:var(--muted);cursor:pointer;transition:all var(--transition);text-align:center}
          .ftab.active,.ftab:hover{background:var(--bg3);color:var(--text)}
          .ftab.active{color:var(--accent)}
          .project-list{overflow-y:auto;flex:1;padding:6px 0}
          .project-item{display:flex;align-items:center;gap:8px;padding:7px 14px;cursor:pointer;transition:background var(--transition);border-left:2px solid transparent}
          .project-item:hover{background:var(--bg3)}
          .project-item.active{background:var(--tag-bg);border-left-color:var(--accent)}
          .risk-dot{width:8px;height:8px;border-radius:50%;flex-shrink:0}
          .risk-High{background:var(--danger)}.risk-Medium{background:var(--warn)}.risk-Low{background:var(--accent2)}
          .project-name{font-family:var(--mono);font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;flex:1}
          .project-tfm{font-size:10px;color:var(--muted);font-family:var(--mono);white-space:nowrap}
          .graph-area{grid-area:graph;position:relative;overflow:hidden;background:var(--bg)}
          #graph-svg{width:100%;height:100%;cursor:grab}
          #graph-svg:active{cursor:grabbing}
          .graph-controls{position:absolute;bottom:16px;right:16px;display:flex;flex-direction:column;gap:4px}
          .graph-btn{width:32px;height:32px;background:var(--bg2);border:1px solid var(--border);border-radius:var(--radius);color:var(--text);font-size:16px;cursor:pointer;display:flex;align-items:center;justify-content:center;transition:background var(--transition)}
          .graph-btn:hover{background:var(--bg3)}
          .graph-legend{position:absolute;bottom:16px;left:16px;background:var(--bg2);border:1px solid var(--border);border-radius:var(--radius);padding:10px 14px;font-size:11px;display:flex;flex-direction:column;gap:5px}
          .legend-item{display:flex;align-items:center;gap:7px;color:var(--muted)}
          .detail-panel{grid-area:panel;background:var(--bg2);border-left:1px solid var(--border);overflow-y:auto;display:flex;flex-direction:column}
          .panel-empty{flex:1;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;color:var(--muted);font-family:var(--mono);font-size:12px;padding:40px;text-align:center}
          .panel-empty svg{opacity:.3}
          .panel-content{padding:20px}
          .panel-title{font-family:var(--mono);font-size:15px;font-weight:700;color:var(--text);margin-bottom:4px;word-break:break-all}
          .panel-path{font-family:var(--mono);font-size:10px;color:var(--muted);word-break:break-all;margin-bottom:14px}
          .risk-badge{display:inline-flex;align-items:center;gap:5px;padding:3px 10px;border-radius:20px;font-size:11px;font-weight:700;font-family:var(--mono);margin-bottom:16px}
          .risk-badge.High{background:rgba(248,81,73,.15);color:var(--danger);border:1px solid rgba(248,81,73,.3)}
          .risk-badge.Medium{background:rgba(210,153,34,.15);color:var(--warn);border:1px solid rgba(210,153,34,.3)}
          .risk-badge.Low{background:rgba(63,185,80,.15);color:var(--accent2);border:1px solid rgba(63,185,80,.3)}
          .section{margin-bottom:20px}
          .section-title{font-size:10px;text-transform:uppercase;letter-spacing:.1em;color:var(--muted);font-weight:600;margin-bottom:8px;display:flex;align-items:center;gap:6px}
          .section-title::after{content:'';flex:1;height:1px;background:var(--border)}
          .meta-grid{display:grid;grid-template-columns:1fr 1fr;gap:6px}
          .meta-item{background:var(--bg3);border:1px solid var(--border);border-radius:var(--radius);padding:8px 10px}
          .meta-label{font-size:9px;text-transform:uppercase;letter-spacing:.08em;color:var(--muted);margin-bottom:2px}
          .meta-value{font-family:var(--mono);font-size:12px;color:var(--text);word-break:break-all}
          .tag-list{display:flex;flex-wrap:wrap;gap:5px}
          .tag{background:var(--tag-bg);border:1px solid var(--border);border-radius:4px;padding:2px 8px;font-size:11px;font-family:var(--mono);color:var(--accent);cursor:pointer;transition:background var(--transition)}
          .tag:hover{background:var(--accent);color:var(--bg)}
          .tag.warn{color:var(--warn);border-color:rgba(210,153,34,.3);background:rgba(210,153,34,.08)}
          .tag.green{color:var(--accent2);border-color:rgba(63,185,80,.3);background:rgba(63,185,80,.08)}
          .issue-list{display:flex;flex-direction:column;gap:4px}
          .issue-item{background:rgba(248,81,73,.08);border-left:2px solid var(--danger);border-radius:0 4px 4px 0;padding:6px 10px;font-size:11px;font-family:var(--mono);color:var(--text)}
          .issue-item.warn{background:rgba(210,153,34,.08);border-left-color:var(--warn)}
          .pkg-list{display:flex;flex-direction:column;gap:3px;max-height:260px;overflow-y:auto}
          .pkg-row{display:flex;align-items:center;gap:6px;background:var(--bg3);border:1px solid var(--border);border-radius:var(--radius);padding:5px 10px;font-size:11px}
          .pkg-name{font-family:var(--mono);color:var(--text);flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
          .pkg-ver{font-family:var(--mono);color:var(--muted);flex-shrink:0}.pkg-usage{font-family:var(--mono);font-size:10px;color:var(--muted);flex-shrink:0;white-space:nowrap}
          .pkg-badges{display:flex;gap:2px;align-items:center;flex-shrink:0}
          .compat-badge{display:inline-flex;align-items:center;padding:1px 5px;border-radius:3px;font-size:9px;font-family:var(--mono);font-weight:700;white-space:nowrap}
          .compat-badge.ok{background:rgba(63,185,80,.12);color:var(--accent2);border:1px solid rgba(63,185,80,.3)}
          .compat-badge.no{background:rgba(248,81,73,.12);color:var(--danger);border:1px solid rgba(248,81,73,.3)}
          .compat-badge.unk{background:var(--bg3);color:var(--muted);border:1px solid var(--border)}
          .node circle{stroke-width:2px;cursor:pointer}
          .node text{font-family:'JetBrains Mono',monospace;font-size:10px;fill:#e6edf3;pointer-events:none;text-anchor:middle;dominant-baseline:middle}
          .link{stroke:#30363d;stroke-width:1.5;fill:none;marker-end:url(#arrow)}
          ::-webkit-scrollbar{width:6px}::-webkit-scrollbar-track{background:transparent}
          ::-webkit-scrollbar-thumb{background:var(--border);border-radius:3px}
          ::-webkit-scrollbar-thumb:hover{background:var(--muted)}
        </style>
        </head>
        <body>
        <div class="app">
          <header>
            <div class="logo">dep<span>·</span>tree <span>// .NET Migration Assessment</span></div>
            <div class="summary-pills" id="summary-pills"></div>
            <div class="header-right">
              <div class="search-wrap">
                <span class="si">⌕</span>
                <input type="text" id="search-input" placeholder="Filter projects…" />
              </div>
            </div>
          </header>
          <aside class="sidebar">
            <div class="sidebar-header">
              <h3>Projects</h3>
              <span id="proj-count" style="font-size:11px;color:var(--muted);font-family:var(--mono)"></span>
            </div>
            <div class="filter-tabs">
              <button class="ftab active" data-filter="all">All</button>
              <button class="ftab" data-filter="High">🔴 High</button>
              <button class="ftab" data-filter="Medium">🟡 Med</button>
              <button class="ftab" data-filter="Low">🟢 Low</button>
            </div>
            <div class="project-list" id="project-list"></div>
          </aside>
          <main class="graph-area">
            <svg id="graph-svg"></svg>
            <div class="graph-controls">
              <button class="graph-btn" id="zoom-in">+</button>
              <button class="graph-btn" id="zoom-out">−</button>
              <button class="graph-btn" id="zoom-reset">⊙</button>
            </div>
            <div class="graph-legend">
              <div class="legend-item"><span class="dot dot-danger"></span>High risk</div>
              <div class="legend-item"><span class="dot dot-warn"></span>Medium risk</div>
              <div class="legend-item"><span class="dot dot-green"></span>Low risk</div>
              <div class="legend-item"><span style="width:16px;height:1px;background:#58a6ff;display:inline-block"></span>References</div>
            </div>
          </main>
          <aside class="detail-panel" id="detail-panel">
            <div class="panel-empty" id="panel-empty">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <div>Select a project to view details</div>
            </div>
            <div class="panel-content" id="panel-content" style="display:none"></div>
          </aside>
        </div>
        <script>
        /*INJECT_DATA*/

        const projectMap = {};
        DATA.projects.forEach(p => { projectMap[p.name] = p; });

        const topPkgMap = {};
        (DATA.summary.topPackages || []).forEach(tp => { topPkgMap[tp.name.toLowerCase()] = tp; });

        function compatBadge(val, label) {
          const cls  = val === true ? 'ok' : val === false ? 'no' : 'unk';
          const icon = val === true ? '✓' : val === false ? '✗' : '?';
          return '<span class="compat-badge ' + cls + '">' + label + ' ' + icon + '</span>';
        }

        let selectedProject = null, filterRisk = 'all', searchQuery = '';
        let transform = { x: 0, y: 0, k: 1 };
        let nodes = [], links = [];
        let isDragging = false, dragNode = null, dragOffX = 0, dragOffY = 0;
        let isPanning = false, panStart = { x: 0, y: 0 };

        const svg = document.getElementById('graph-svg');

        function riskColor(l) {
          return l === 'High' ? '#f85149' : l === 'Medium' ? '#d29922' : '#3fb950';
        }

        function renderSummaryPills() {
          const s = DATA.summary;
          const pkgs = s.topPackages || [];
          const net8ok = pkgs.filter(p => p.supportsNet8 === true).length;
          const net8no = pkgs.filter(p => p.supportsNet8 === false).length;
          const pills = [
            `<span class="pill"><span class="dot dot-blue"></span>${s.totalProjects} projects</span>`,
            `<span class="pill"><span class="dot dot-muted"></span>${s.testProjects} tests</span>`,
            `<span class="pill"><span class="dot dot-danger"></span>${s.riskSummary.high} high</span>`,
            `<span class="pill"><span class="dot dot-warn"></span>${s.riskSummary.medium} medium</span>`,
            `<span class="pill"><span class="dot dot-green"></span>${s.riskSummary.low} low</span>`,
            ...(s.frameworkSummary||[]).map(f=>`<span class="pill"><span class="dot dot-muted"></span>${f.count} ${f.class}</span>`),
          ];
          if (pkgs.length) pills.push(`<span class="pill"><span class="dot dot-muted"></span>${pkgs.length} packages</span>`);
          if (net8ok)      pills.push(`<span class="pill"><span class="dot dot-green"></span>${net8ok} NET8 ready</span>`);
          if (net8no)      pills.push(`<span class="pill"><span class="dot dot-danger"></span>${net8no} NET8 issues</span>`);
          document.getElementById('summary-pills').innerHTML = pills.join('');
        }

        function renderProjectList() {
          const q = searchQuery.toLowerCase();
          const filtered = DATA.projects.filter(p =>
            (filterRisk === 'all' || p.migrationRisk.level === filterRisk) &&
            (!q || p.name.toLowerCase().includes(q))
          );
          document.getElementById('proj-count').textContent = `${filtered.length} / ${DATA.projects.length}`;
          document.getElementById('project-list').innerHTML = filtered.map(p => `
            <div class="project-item ${selectedProject===p.name?'active':''}" data-name="${p.name}" onclick="selectProject('${p.name}')">
              <span class="risk-dot risk-${p.migrationRisk.level}"></span>
              <span class="project-name">${p.name}</span>
              <span class="project-tfm">${(p.targetFrameworks||[]).join(', ')}</span>
            </div>`).join('');
        }

        function selectProject(name) {
          selectedProject = name;
          renderProjectList();
          renderDetailPanel(projectMap[name]);
          highlightNode(name);
        }

        function tag(label, cls, onClick) {
          const oc = onClick ? ' onclick="' + onClick + '"' : '';
          return '<span class="tag ' + cls + '"' + oc + '>' + label + '</span>';
        }
        function metaItem(label, value) {
          return '<div class="meta-item"><div class="meta-label">' + label + '</div><div class="meta-value">' + value + '</div></div>';
        }
        function section(title, body) {
          return '<div class="section"><div class="section-title">' + title + '</div>' + body + '</div>';
        }

        function renderDetailPanel(p) {
          document.getElementById('panel-empty').style.display = 'none';
          const el = document.getElementById('panel-content');
          el.style.display = 'block';
          const risk = p.migrationRisk;
          const pkgs = p.packages || [];
          const projRefs = p.projectRefs || [];
          const dependents = p.dependents || [];

          const riskIcon = risk.level === 'High' ? '⚠' : risk.level === 'Medium' ? '◐' : '✓';

          let html = '';
          html += '<div class="panel-title">' + p.name + '</div>';
          html += '<div class="panel-path">' + (p.relativePath || p.path) + '</div>';
          html += '<div class="risk-badge ' + risk.level + '">' + riskIcon + ' ' + risk.level + ' Migration Risk</div>';

          if (risk.issues && risk.issues.length) {
            const items = risk.issues.map(function(i) {
              const cls = (i.indexOf('EF6') >= 0 || i.indexOf('consider') >= 0) ? 'warn' : '';
              return '<div class="issue-item ' + cls + '">' + i + '</div>';
            }).join('');
            html += section('Migration Issues', '<div class="issue-list">' + items + '</div>');
          }

          const meta = [
            ['Output Type',     p.outputType || '—'],
            ['Project Style',   p.projectStyle || '—'],
            ['Framework(s)',    (p.targetFrameworks || []).join(', ') || '—'],
            ['Framework Class', p.frameworkClass || '—'],
            ['Assembly',        p.assemblyName || '—'],
            ['Lang Version',    p.langVersion || '—'],
            ['Nullable',        p.nullable || '—'],
            ['.cs Files',       p.csFileCount != null ? p.csFileCount : '—'],
            ['Dockerfile',      p.hasDockerfile ? '✓ Yes' : '✗ No'],
            ['Test Project',    p.isTestProject ? '✓ Yes' : '✗ No'],
            ['web.config',      (p.configFiles && p.configFiles.webConfig) ? '✓ Yes' : '✗ No'],
            ['appsettings',     (p.configFiles && p.configFiles.appSettings) ? '✓ Yes' : '✗ No'],
          ];
          const metaHtml = meta.map(function(m) { return metaItem(m[0], m[1]); }).join('');
          html += section('Project Info', '<div class="meta-grid">' + metaHtml + '</div>');

          if (projRefs.length) {
            const tags = projRefs.map(function(r) {
              const ext = r.indexOf('(external)') >= 0;
              return tag(r, ext ? 'warn' : '', ext ? '' : "selectProject('" + r + "')");
            }).join('');
            html += section('References (' + projRefs.length + ')', '<div class="tag-list">' + tags + '</div>');
          }

          if (dependents.length) {
            const tags = dependents.map(function(d) {
              return tag(d, 'green', "selectProject('" + d + "')");
            }).join('');
            html += section('Used by (' + dependents.length + ')', '<div class="tag-list">' + tags + '</div>');
          }

          if (pkgs.length) {
            const rows = pkgs.map(function(pkg) {
              const tp = topPkgMap[pkg.name.toLowerCase()];
              const usageHtml = tp && tp.usedBy > 1
                ? '<span class="pkg-usage">' + tp.usedBy + ' projs</span>'
                : '';
              const badgesHtml = '<span class="pkg-badges">'
                + compatBadge(pkg.supportsNet8, 'NET8')
                + compatBadge(pkg.supportsNet10, 'NET10')
                + '</span>';
              return '<div class="pkg-row">'
                + '<span class="pkg-name">' + pkg.name + '</span>'
                + badgesHtml
                + usageHtml
                + '<span class="pkg-ver">' + (pkg.version || '') + '</span>'
                + '</div>';
            }).join('');
            html += section('NuGet Packages (' + pkgs.length + ')', '<div class="pkg-list">' + rows + '</div>');
          }

          el.innerHTML = html;
        }

        function buildGraph() {
          const W = svg.clientWidth || 900, H = svg.clientHeight || 600;
          const nodeMap = {};
          nodes = DATA.projects.map(p => ({
            id: p.name, risk: p.migrationRisk.level,
            x: W/2 + (Math.random()-.5)*400, y: H/2 + (Math.random()-.5)*300,
            vx: 0, vy: 0
          }));
          nodes.forEach(n => nodeMap[n.id] = n);
          links = [];
          DATA.projects.forEach(p => {
            (p.projectRefs||[]).forEach(r => {
              const clean = r.replace(' (external)','');
              if (nodeMap[p.name] && nodeMap[clean]) links.push({source:p.name, target:clean});
            });
          });
          const k = Math.sqrt(W*H/Math.max(nodes.length,1)) * 1.2;
          for (let iter = 0; iter < 150; iter++) {
            const cool = 1 - iter/150, temp = 80*cool*cool;
            for (let i = 0; i < nodes.length; i++) for (let j = i+1; j < nodes.length; j++) {
              const a = nodes[i], b = nodes[j];
              const dx = b.x-a.x, dy = b.y-a.y, dist = Math.max(Math.sqrt(dx*dx+dy*dy),.01);
              const f = k*k/dist, fx = dx/dist*f, fy = dy/dist*f;
              a.vx-=fx; a.vy-=fy; b.vx+=fx; b.vy+=fy;
            }
            links.forEach(l => {
              const a = nodeMap[l.source], b = nodeMap[l.target];
              if (!a||!b) return;
              const dx=b.x-a.x,dy=b.y-a.y,dist=Math.max(Math.sqrt(dx*dx+dy*dy),.01);
              const f=dist*dist/k*.3,fx=dx/dist*f,fy=dy/dist*f;
              a.vx+=fx;a.vy+=fy;b.vx-=fx;b.vy-=fy;
            });
            nodes.forEach(n => {
              n.vx+=(W/2-n.x)*.01; n.vy+=(H/2-n.y)*.01;
              const spd=Math.sqrt(n.vx*n.vx+n.vy*n.vy);
              if(spd>temp){n.vx=n.vx/spd*temp;n.vy=n.vy/spd*temp;}
              n.x=Math.max(40,Math.min(W-40,n.x+n.vx));
              n.y=Math.max(40,Math.min(H-40,n.y+n.vy));
              n.vx*=.8;n.vy*=.8;
            });
          }
          renderGraph();
        }

        function renderGraph() {
          const nodeMap = {};
          nodes.forEach(n => nodeMap[n.id] = n);
          svg.innerHTML = `<defs>
            <marker id="arrow" markerWidth="6" markerHeight="6" refX="5" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L0,6 L6,3 z" fill="#30363d"/></marker>
            <marker id="arrow-hl" markerWidth="6" markerHeight="6" refX="5" refY="3" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L0,6 L6,3 z" fill="#58a6ff"/></marker>
          </defs>
          <g id="graph-root" transform="translate(${transform.x},${transform.y}) scale(${transform.k})">
            <g id="links-layer"></g><g id="nodes-layer"></g>
          </g>`;
          const ll = document.getElementById('links-layer');
          links.forEach(l => {
            const a=nodeMap[l.source],b=nodeMap[l.target]; if(!a||!b) return;
            const dx=b.x-a.x,dy=b.y-a.y,dist=Math.sqrt(dx*dx+dy*dy),r=14;
            const line=document.createElementNS('http://www.w3.org/2000/svg','line');
            line.setAttribute('class','link');
            line.setAttribute('x1',a.x);line.setAttribute('y1',a.y);
            line.setAttribute('x2',b.x-dx/dist*r);line.setAttribute('y2',b.y-dy/dist*r);
            line.dataset.source=l.source;line.dataset.target=l.target;
            ll.appendChild(line);
          });
          const nl = document.getElementById('nodes-layer');
          nodes.forEach(n => {
            const g=document.createElementNS('http://www.w3.org/2000/svg','g');
            g.setAttribute('class','node');g.setAttribute('transform',`translate(${n.x},${n.y})`);g.dataset.id=n.id;
            const c=document.createElementNS('http://www.w3.org/2000/svg','circle');
            c.setAttribute('r',14);c.setAttribute('fill',riskColor(n.risk)+'22');c.setAttribute('stroke',riskColor(n.risk));
            g.appendChild(c);
            const t=document.createElementNS('http://www.w3.org/2000/svg','text');
            t.setAttribute('y',26);t.textContent=n.id.length>12?n.id.substring(0,10)+'…':n.id;
            g.appendChild(t);
            g.addEventListener('click',e=>{e.stopPropagation();selectProject(n.id);});
            g.addEventListener('mousedown',e=>{
              e.stopPropagation();isDragging=true;dragNode=n;
              const pt=svgPoint(e);dragOffX=pt.x-n.x;dragOffY=pt.y-n.y;svg.style.cursor='grabbing';
            });
            nl.appendChild(g);
          });
          svg.addEventListener('mousedown',e=>{
            if(!isDragging){isPanning=true;panStart={x:e.clientX-transform.x,y:e.clientY-transform.y};svg.style.cursor='grabbing';}
          });
          svg.addEventListener('mousemove',e=>{
            if(isDragging&&dragNode){const pt=svgPoint(e);dragNode.x=pt.x-dragOffX;dragNode.y=pt.y-dragOffY;updatePositions();}
            else if(isPanning){transform.x=e.clientX-panStart.x;transform.y=e.clientY-panStart.y;applyTransform();}
          });
          svg.addEventListener('mouseup',()=>{isDragging=false;dragNode=null;isPanning=false;svg.style.cursor='grab';});
          svg.addEventListener('wheel',e=>{
            e.preventDefault();
            transform.k=Math.max(.2,Math.min(4,transform.k*(e.deltaY>0?.9:1.1)));applyTransform();
          },{passive:false});
        }

        function svgPoint(e){const r=svg.getBoundingClientRect();return{x:(e.clientX-r.left-transform.x)/transform.k,y:(e.clientY-r.top-transform.y)/transform.k};}
        function applyTransform(){const r=document.getElementById('graph-root');if(r)r.setAttribute('transform',`translate(${transform.x},${transform.y}) scale(${transform.k})`);}
        function updatePositions(){
          const nm={};nodes.forEach(n=>nm[n.id]=n);
          document.querySelectorAll('.node').forEach(g=>{const n=nm[g.dataset.id];if(n)g.setAttribute('transform',`translate(${n.x},${n.y})`);});
          document.querySelectorAll('.link').forEach(l=>{
            const a=nm[l.dataset.source],b=nm[l.dataset.target];if(!a||!b)return;
            const dx=b.x-a.x,dy=b.y-a.y,dist=Math.max(Math.sqrt(dx*dx+dy*dy),.01),r=14;
            l.setAttribute('x1',a.x);l.setAttribute('y1',a.y);l.setAttribute('x2',b.x-dx/dist*r);l.setAttribute('y2',b.y-dy/dist*r);
          });
        }
        function highlightNode(name){
          document.querySelectorAll('.node circle').forEach(c=>c.setAttribute('stroke-width','2'));
          document.querySelectorAll('.link').forEach(l=>{l.style.stroke='#30363d';l.setAttribute('marker-end','url(#arrow)');});
          const g=document.querySelector(`.node[data-id="${name}"]`);
          if(g){
            g.querySelector('circle').setAttribute('stroke-width','3');
            document.querySelectorAll('.link').forEach(l=>{
              if(l.dataset.source===name||l.dataset.target===name){l.style.stroke='#58a6ff';l.setAttribute('marker-end','url(#arrow-hl)');}
            });
            const W=svg.clientWidth,H=svg.clientHeight,n=nodes.find(nd=>nd.id===name);
            if(n){transform.x=W/2-n.x*transform.k;transform.y=H/2-n.y*transform.k;applyTransform();}
          }
        }

        document.querySelectorAll('.ftab').forEach(btn=>btn.addEventListener('click',()=>{
          document.querySelectorAll('.ftab').forEach(b=>b.classList.remove('active'));
          btn.classList.add('active');filterRisk=btn.dataset.filter;renderProjectList();
        }));
        document.getElementById('search-input').addEventListener('input',e=>{searchQuery=e.target.value;renderProjectList();});
        document.getElementById('zoom-in').addEventListener('click',()=>{transform.k=Math.min(4,transform.k*1.2);applyTransform();});
        document.getElementById('zoom-out').addEventListener('click',()=>{transform.k=Math.max(.2,transform.k/1.2);applyTransform();});
        document.getElementById('zoom-reset').addEventListener('click',()=>{transform={x:0,y:0,k:1};applyTransform();});

        renderSummaryPills();
        renderProjectList();
        requestAnimationFrame(()=>requestAnimationFrame(()=>buildGraph()));
        window.addEventListener('resize',()=>buildGraph());
        </script>
        </body>
        </html>
        """;
}

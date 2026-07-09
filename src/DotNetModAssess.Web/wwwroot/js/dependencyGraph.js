// JS interop module backing Components/Graph/DependencyGraphView.razor.
//
// Cytoscape.js itself is loaded globally via a CDN <script> tag in App.razor (see the project's
// tech-stack decision to CDN-load rather than npm-install/vendor front-end deps), so this module
// just references the global `cytoscape` function.
//
// The C# side never reaches into Cytoscape internals directly: `init()` builds a small wrapper
// API object (highlight/clearHighlight/applyFilter/dispose) and that object - not the raw
// cytoscape core - is what gets handed back to .NET as an IJSObjectReference.

const NODE_STYLE = [
    {
        selector: "node",
        style: {
            label: "data(label)",
            "font-size": 10,
            color: "#1b1b1b",
            "text-valign": "center",
            "text-halign": "center",
            "text-wrap": "wrap",
            "text-max-width": "120px",
            "background-color": "#6c9bd1",
            width: "label",
            height: "label",
            padding: "10px",
            "border-width": 1,
            "border-color": "#345a82"
        }
    },
    {
        // Project nodes: rectangular "box" shape.
        selector: 'node[kind = "project"]',
        style: {
            shape: "round-rectangle",
            "background-color": "#3b6fb6",
            color: "#ffffff",
            "border-color": "#1f3f66"
        }
    },
    {
        // Package nodes: ellipse shape.
        selector: 'node[kind = "package"]',
        style: {
            shape: "ellipse",
            "background-color": "#8a8f98",
            color: "#ffffff",
            "border-color": "#54585f"
        }
    },
    {
        // Version-conflicted packages get a clearly distinct (red/orange) treatment.
        selector: "node[?conflict]",
        style: {
            "background-color": "#d9534f",
            "border-color": "#7a1f1a",
            "border-width": 3
        }
    },
    {
        selector: "edge",
        style: {
            width: 1.5,
            "line-color": "#adb5bd",
            "target-arrow-color": "#adb5bd",
            "target-arrow-shape": "triangle",
            "curve-style": "bezier",
            opacity: 0.85
        }
    },
    {
        selector: 'edge[kind = "ProjectToProject"]',
        style: {
            "line-color": "#495057",
            "target-arrow-color": "#495057"
        }
    },
    {
        // Click-to-focus: everything not in the highlighted neighborhood fades out.
        selector: ".faded",
        style: {
            opacity: 0.1
        }
    },
    {
        selector: "node.highlighted",
        style: {
            opacity: 1
        }
    },
    {
        selector: "edge.highlighted",
        style: {
            opacity: 1,
            width: 2.5
        }
    },
    {
        selector: "node.focus",
        style: {
            "border-width": 4,
            "border-color": "#212529"
        }
    },
    {
        // Filtering: hidden nodes/edges are removed from layout via display:none, without
        // touching the underlying element set (so re-showing them is instant, no re-init).
        selector: ".hidden",
        style: {
            display: "none"
        }
    }
];

function toElements(nodes, edges) {
    const nodeElements = nodes.map((n) => ({
        data: {
            id: n.id,
            kind: n.kind,
            label: n.label,
            conflict: n.hasVersionConflict,
            tfms: n.tfms || []
        }
    }));

    const edgeElements = edges.map((e) => ({
        data: {
            id: e.id,
            kind: e.kind,
            source: e.source,
            target: e.target
        }
    }));

    return [...nodeElements, ...edgeElements];
}

export function init(container, nodes, edges, dotNetRef) {
    const cy = cytoscape({
        container,
        elements: toElements(nodes, edges),
        style: NODE_STYLE,
        layout: {
            name: "cose",
            animate: false,
            nodeDimensionsIncludeLabels: true,
            padding: 30
        },
        wheelSensitivity: 0.2
    });

    cy.on("tap", "node", (evt) => {
        const id = evt.target.id();
        dotNetRef.invokeMethodAsync("OnNodeClicked", id);
    });

    // Tapping empty canvas (the core itself, not an element) clears the focus/highlight.
    cy.on("tap", (evt) => {
        if (evt.target === cy) {
            dotNetRef.invokeMethodAsync("OnBackgroundClicked");
        }
    });

    return {
        // Click-to-focus: `focusId` is the clicked node; `neighborhoodIds` is the full set of
        // node ids (upstream + downstream) that should stay fully visible. Everything else fades.
        highlight(focusId, neighborhoodIds) {
            cy.batch(() => {
                cy.elements().removeClass("faded highlighted focus");

                if (!focusId) {
                    return;
                }

                const keep = new Set(neighborhoodIds || []);
                keep.add(focusId);

                cy.nodes().forEach((node) => {
                    if (keep.has(node.id())) {
                        node.addClass("highlighted");
                    } else {
                        node.addClass("faded");
                    }
                });

                cy.edges().forEach((edge) => {
                    const data = edge.data();
                    if (keep.has(data.source) && keep.has(data.target)) {
                        edge.addClass("highlighted");
                    } else {
                        edge.addClass("faded");
                    }
                });

                const focusNode = cy.getElementById(focusId);
                if (focusNode && focusNode.length > 0) {
                    focusNode.removeClass("faded").addClass("focus highlighted");
                }
            });
        },

        clearHighlight() {
            cy.elements().removeClass("faded highlighted focus");
        },

        // Independent per-kind substring/TFM filters (see DependencyGraphView.razor for the
        // documented semantics): project nodes are matched against projectSubstring + tfm,
        // package nodes are matched against packageSubstring. An edge is visible only if both
        // its endpoints are visible.
        applyFilter(projectSubstring, packageSubstring, tfm) {
            const projectNeedle = (projectSubstring || "").trim().toLowerCase();
            const packageNeedle = (packageSubstring || "").trim().toLowerCase();
            const tfmNeedle = tfm || "";

            cy.batch(() => {
                cy.nodes().forEach((node) => {
                    const data = node.data();
                    let visible = true;

                    if (data.kind === "project") {
                        if (projectNeedle && !data.label.toLowerCase().includes(projectNeedle)) {
                            visible = false;
                        }
                        if (tfmNeedle && !(data.tfms || []).includes(tfmNeedle)) {
                            visible = false;
                        }
                    } else {
                        if (packageNeedle && !data.label.toLowerCase().includes(packageNeedle)) {
                            visible = false;
                        }
                    }

                    node.toggleClass("hidden", !visible);
                });

                cy.edges().forEach((edge) => {
                    const hidden = edge.source().hasClass("hidden") || edge.target().hasClass("hidden");
                    edge.toggleClass("hidden", hidden);
                });
            });
        },

        dispose() {
            cy.destroy();
        }
    };
}

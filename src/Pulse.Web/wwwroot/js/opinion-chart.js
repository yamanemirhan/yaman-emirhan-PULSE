import "../vendor/chartjs/chart.umd.min.js";

const charts = new Map();
const stamp = new Intl.DateTimeFormat("en-GB", {
    day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit", second: "2-digit",
    hourCycle: "h23", timeZoneName: "shortOffset"
});
const label = iso => stamp.format(new Date(iso)).replace("GMT", "UTC");

// Evidence lines share the same activity index as the data, including equal timestamps.
const evidenceLines = {
    id: "pulseEvidence",
    beforeDatasetsDraw(chart, _args, options) {
        const {ctx, chartArea, scales} = chart;
        if (!chartArea) return;
        ctx.save();
        ctx.font = "11px 'Segoe UI', sans-serif";
        ctx.textAlign = "center";
        let lastLabelX = -Infinity;
        for (const marker of options.markers ?? []) {
            const x = scales.x.getPixelForValue(marker.index);
            ctx.strokeStyle = "#b4beb6";
            ctx.setLineDash([3, 4]);
            ctx.beginPath(); ctx.moveTo(x, chartArea.top); ctx.lineTo(x, chartArea.bottom); ctx.stroke();
            // Keep close labels legible; every marker remains in the accessible evidence list.
            if (x - lastLabelX > 28) {
                ctx.fillStyle = "#4c6156";
                ctx.fillText(marker.label, x, chartArea.top - 9);
                lastLabelX = x;
            }
        }
        ctx.restore();
    }
};

export function render(id, canvas, points, markers, answer) {
    if (!canvas?.isConnected) return;
    const color = {Yes: "#245b48", No: "#a55542", Unsure: "#8b6922"}[answer];
    const indices = new Map(points.map((point, index) => [point.sequence, index]));
    const data = {
        labels: points.map(p => label(p.timeUtc)),
        datasets: [
            {label: `${answer} · equal votes`, data: points.map(p => p.share), borderColor: "#84958b", borderDash: [6, 4], borderWidth: 2},
            {label: `${answer} · confidence-weighted`, data: points.map(p => p.weighted), borderColor: color, borderWidth: 3}
        ]
    };
    for (const dataset of data.datasets) Object.assign(dataset, {
        stepped: "before", spanGaps: false, pointRadius: points.length > 80 ? 0 : 2,
        pointHitRadius: 10, pointHoverRadius: 5, backgroundColor: dataset.borderColor
    });
    const options = {
        responsive: true, maintainAspectRatio: false, animation: false,
        interaction: {mode: "index", intersect: false},
        layout: {padding: {top: 22, right: 14, left: 8}},
        scales: {
            x: {type: "category", offset: true, grid: {display: false},
                title: {display: true, text: "Recorded activity · equally spaced"},
                ticks: {maxTicksLimit: 5, maxRotation: 0, color: "#64706a"}},
            y: {min: 0, max: 100, ticks: {stepSize: 25, callback: value => `${value}%`},
                grid: {color: "#e8ebe5"}, title: {display: true, text: `${answer} share`}}
        },
        plugins: {
            legend: {position: "bottom", labels: {boxWidth: 28, padding: 16, color: "#34483d"}},
            pulseEvidence: {markers: markers.map(m => ({index: indices.get(m.sequence), label: m.label}))},
            tooltip: {callbacks: {
                title: items => {
                    const p = points[items[0].dataIndex];
                    return `${label(p.timeUtc)} · activity #${items[0].dataIndex + 1}`;
                },
                label: context => `${context.dataset.label}: ${context.parsed.y?.toFixed(1) ?? "Unavailable"}%`,
                afterBody: items => {
                    const p = points[items[0].dataIndex];
                    const evidence = markers.filter(m => m.sequence === p.sequence);
                    return [`${p.participants} participants · ${p.activity}`,
                        ...evidence.map(m => `${m.label}: ${m.action}`)];
                }
            }}
        }
    };
    const existing = charts.get(id);
    if (existing) {
        existing.chart.data = data;
        existing.chart.options = options;
        existing.chart.update("none");
        return;
    }
    const chart = new globalThis.Chart(canvas, {type: "line", data, options, plugins: [evidenceLines]});
    const observer = new MutationObserver(() => { if (!canvas.isConnected) destroy(id); });
    observer.observe(document.body, {childList: true, subtree: true});
    charts.set(id, {chart, observer});
}

export function destroy(id) {
    const item = charts.get(id);
    if (!item) return;
    item.observer.disconnect();
    item.chart.destroy();
    charts.delete(id);
}

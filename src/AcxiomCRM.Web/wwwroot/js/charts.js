// Dashboard & report charts (Chart.js). Data comes from server-authorized JSON embedded as
// <script type="application/json" id="..."> blocks — never fetched with client-side scope.
(function () {
  'use strict';
  if (!window.Chart) return;

  const css = getComputedStyle(document.documentElement);
  const token = (name, fallback) => (css.getPropertyValue(name) || '').trim() || fallback;
  const SERIES = token('--chart-series-1', '#2a78d6');
  const GRID = token('--chart-grid', '#e1e0d9');
  const AXIS = token('--chart-axis', '#898781');

  Chart.defaults.font.family = 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif';
  Chart.defaults.font.size = 12;
  Chart.defaults.color = AXIS;
  Chart.defaults.plugins.legend.display = false; // single-series charts: the card title names the series
  Chart.defaults.plugins.tooltip.backgroundColor = '#0f1d33';
  Chart.defaults.plugins.tooltip.padding = 10;
  Chart.defaults.plugins.tooltip.cornerRadius = 8;
  Chart.defaults.plugins.tooltip.displayColors = false;
  Chart.defaults.interaction = { mode: 'index', intersect: false };
  Chart.defaults.maintainAspectRatio = false;
  Chart.defaults.animation.duration = 400;

  const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 });
  const compact = (v) => v >= 1e7 ? '₹' + (v / 1e7).toFixed(1) + ' Cr' : v >= 1e5 ? '₹' + (v / 1e5).toFixed(1) + ' L' : inr.format(v);

  const bar = {
    backgroundColor: SERIES,
    hoverBackgroundColor: '#256abf',
    borderRadius: 4,            // rounded data end only (borderSkipped keeps the baseline square)
    borderSkipped: 'start',
    maxBarThickness: 34,
    categoryPercentage: 0.7,
    barPercentage: 0.9
  };
  const grid = { color: GRID, drawTicks: false };
  const noGrid = { display: false };

  function read(id) {
    const el = document.getElementById(id);
    if (!el) return null;
    try { return JSON.parse(el.textContent); } catch { return null; }
  }

  function make(id, config) {
    const canvas = document.getElementById(id);
    if (canvas) new Chart(canvas, config);
  }

  const d = read('dashboard-data');
  if (d) {
    make('leadStatusChart', {
      type: 'bar',
      data: { labels: d.leadStatus.labels, datasets: [{ label: 'Leads', data: d.leadStatus.values, ...bar }] },
      options: {
        indexAxis: 'y',
        scales: { x: { grid, beginAtZero: true, ticks: { precision: 0, padding: 6 }, border: { display: false } }, y: { grid: noGrid, border: { color: GRID } } },
        plugins: { tooltip: { callbacks: { label: (c) => `${c.parsed.x} lead${c.parsed.x === 1 ? '' : 's'}` } } }
      }
    });

    make('pipelineChart', {
      type: 'bar',
      data: { labels: d.pipeline.labels, datasets: [{ label: 'Amount', data: d.pipeline.amounts, ...bar }] },
      options: {
        scales: { y: { grid, beginAtZero: true, ticks: { callback: compact, padding: 6 }, border: { display: false } }, x: { grid: noGrid, border: { color: GRID } } },
        plugins: {
          tooltip: {
            callbacks: {
              label: (c) => `Amount: ${inr.format(c.parsed.y)}`,
              afterLabel: (c) => `Opportunities: ${d.pipeline.counts[c.dataIndex]}`
            }
          }
        }
      }
    });

    make('monthlyChart', {
      type: 'bar',
      data: { labels: d.monthly.labels, datasets: [{ label: 'Won amount', data: d.monthly.won, ...bar }] },
      options: {
        scales: { y: { grid, beginAtZero: true, ticks: { callback: compact, padding: 6 }, border: { display: false } }, x: { grid: noGrid, border: { color: GRID }, ticks: { maxRotation: 0, autoSkip: true } } },
        plugins: {
          tooltip: {
            callbacks: {
              label: (c) => `Won: ${inr.format(c.parsed.y)}`,
              afterLabel: (c) => `Deals won: ${d.monthly.wonCount[c.dataIndex]} · lost: ${d.monthly.lostCount[c.dataIndex]}`
            }
          }
        }
      }
    });
  }

  const p = read('pipeline-data');
  if (p) {
    make('pipelineReportChart', {
      type: 'bar',
      data: { labels: p.labels, datasets: [{ label: 'Amount', data: p.amounts, ...bar }] },
      options: {
        scales: { y: { grid, beginAtZero: true, ticks: { callback: compact }, border: { display: false } }, x: { grid: noGrid, border: { color: GRID } } },
        plugins: {
          tooltip: {
            callbacks: {
              label: (c) => `Amount: ${inr.format(c.parsed.y)}`,
              afterLabel: (c) => `Weighted: ${inr.format(p.weighted[c.dataIndex])}`
            }
          }
        }
      }
    });
  }
})();

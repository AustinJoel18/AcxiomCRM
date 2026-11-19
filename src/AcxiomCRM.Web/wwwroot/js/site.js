// AcxiomCRM — shared UI behaviour (no inline scripts: the CSP forbids them).
(function () {
  'use strict';

  // Confirmation dialog for destructive actions: <form data-confirm="Delete this customer?">
  const modalEl = document.getElementById('confirmModal');
  const modal = modalEl && window.bootstrap ? new bootstrap.Modal(modalEl) : null;
  let pendingForm = null;

  document.addEventListener('submit', function (e) {
    const form = e.target;
    if (!(form instanceof HTMLFormElement) || !form.dataset.confirm) return;
    if (form.dataset.confirmed === 'true') return;
    e.preventDefault();
    if (!modal) {
      if (window.confirm(form.dataset.confirm)) { form.dataset.confirmed = 'true'; form.submit(); }
      return;
    }
    pendingForm = form;
    modalEl.querySelector('[data-confirm-message]').textContent = form.dataset.confirm;
    const okBtn = modalEl.querySelector('[data-confirm-ok]');
    okBtn.textContent = form.dataset.confirmButton || 'Delete';
    okBtn.className = 'btn ' + (form.dataset.confirmClass || 'btn-danger');
    modal.show();
  });

  if (modalEl) {
    modalEl.querySelector('[data-confirm-ok]').addEventListener('click', function () {
      if (!pendingForm) return;
      pendingForm.dataset.confirmed = 'true';
      modal.hide();
      if (typeof pendingForm.requestSubmit === 'function') pendingForm.requestSubmit(); else pendingForm.submit();
      pendingForm = null;
    });
  }

  // Auto-submit filter selects: <select data-autosubmit>
  document.addEventListener('change', function (e) {
    const el = e.target;
    if (el instanceof HTMLSelectElement && el.hasAttribute('data-autosubmit') && el.form) el.form.submit();
  });

  // Show/hide sections driven by a checkbox: <input type="checkbox" data-toggle-target="#id" [data-toggle-invert]>
  function syncToggle(cb) {
    const target = document.querySelector(cb.dataset.toggleTarget);
    if (!target) return;
    const show = cb.hasAttribute('data-toggle-invert') ? !cb.checked : cb.checked;
    target.classList.toggle('d-none', !show);
  }
  document.querySelectorAll('input[type=checkbox][data-toggle-target]').forEach(function (cb) {
    syncToggle(cb);
    cb.addEventListener('change', function () { syncToggle(cb); });
  });

  // Custom date range on the dashboard: reveal inputs when "custom" is selected.
  document.querySelectorAll('[data-period-custom]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      const panel = document.getElementById('customRange');
      if (panel) panel.classList.toggle('d-none');
    });
  });

  // Opportunity form: default probability when the stage changes (user can still edit it).
  const stage = document.querySelector('select[name="Stage"][data-probability-target]');
  if (stage) {
    const defaults = { Qualification: 20, Proposal: 50, Negotiation: 75, Won: 100, Lost: 0 };
    const prob = document.querySelector(stage.dataset.probabilityTarget);
    const closeReason = document.getElementById('closeReasonGroup');
    const sync = function (fromUser) {
      if (fromUser && prob && defaults[stage.value] !== undefined) prob.value = defaults[stage.value];
      if (prob) prob.readOnly = stage.value === 'Won' || stage.value === 'Lost';
      if (closeReason) closeReason.classList.toggle('d-none', !(stage.value === 'Won' || stage.value === 'Lost'));
    };
    sync(false);
    stage.addEventListener('change', function () { sync(true); });
  }

  // Bootstrap tooltips
  if (window.bootstrap) {
    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function (el) { new bootstrap.Tooltip(el); });
  }
})();

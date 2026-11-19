// Lead conversion: the "use existing customer" switch shows the picker and hides the new-customer fields.
(function () {
  'use strict';
  const toggle = document.getElementById('UseExistingCustomer');
  const mirror = document.getElementById('newCustomerMirror');
  if (!toggle || !mirror) return;
  const sync = () => { mirror.checked = toggle.checked; mirror.dispatchEvent(new Event('change')); };
  toggle.addEventListener('change', sync);
  sync();
})();

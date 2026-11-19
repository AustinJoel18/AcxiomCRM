// Client-side adapters for the custom server validation attributes.
// The server repeats every rule: this file only improves the user experience.
(function ($) {
  'use strict';
  if (!$ || !$.validator || !$.validator.unobtrusive) return;

  function pad(n) { return String(n).padStart(2, '0'); }
  function todayIso() {
    const d = new Date();
    return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
  }

  // True when the dependent field (e.g. Stage) currently holds an "inactive" value (Won/Lost).
  function isInactive(element, params) {
    if (!params || !params.dependent) return false;
    const dep = $(element.form).find('[name="' + params.dependent + '"]');
    const inactive = (params.inactive || '').split(',').filter(Boolean);
    return inactive.indexOf(String(dep.val())) >= 0;
  }

  // [NotBeforeToday] — dates (yyyy-MM-dd) and datetime-local values (yyyy-MM-ddTHH:mm).
  $.validator.addMethod('notbeforetoday', function (value, element, params) {
    if (this.optional(element) || isInactive(element, params)) return true;
    return String(value).substring(0, 10) >= todayIso();
  });
  $.validator.unobtrusive.adapters.add('notbeforetoday', ['dependent', 'inactive'], function (options) {
    options.rules.notbeforetoday = { dependent: options.params.dependent, inactive: options.params.inactive };
    options.messages.notbeforetoday = options.message;
  });

  // [PositiveWhenActive] — amount must be > 0 unless the opportunity is Won/Lost.
  $.validator.addMethod('positivewhenactive', function (value, element, params) {
    if (this.optional(element) || isInactive(element, params)) return true;
    const n = parseFloat(value);
    return !isNaN(n) && n > 0;
  });
  $.validator.unobtrusive.adapters.add('positivewhenactive', ['dependent', 'inactive'], function (options) {
    options.rules.positivewhenactive = { dependent: options.params.dependent, inactive: options.params.inactive };
    options.messages.positivewhenactive = options.message;
  });

  // Re-validate dependent fields when the controlling field changes.
  $(document).on('change', 'select', function () {
    const name = this.name;
    const form = $(this.form);
    if (!form.length || !form.data('validator')) return;
    form.find('[data-val-notbeforetoday-dependent="' + name + '"],[data-val-positivewhenactive-dependent="' + name + '"]')
      .each(function () { if ($(this).val()) form.validate().element(this); });
  });

  // Built-in rules triggered by input types use the same wording as the server.
  $.extend($.validator.messages, {
    email: 'Enter a valid email address.',
    number: 'Enter a valid number.',
    min: $.validator.format('Enter a value greater than or equal to {0}.'),
    max: $.validator.format('Enter a value less than or equal to {0}.')
  });

  // Bootstrap styling for invalid fields.
  $.validator.setDefaults({
    highlight: function (el) { $(el).addClass('is-invalid input-validation-error'); },
    unhighlight: function (el) { $(el).removeClass('is-invalid input-validation-error'); }
  });
})(window.jQuery);

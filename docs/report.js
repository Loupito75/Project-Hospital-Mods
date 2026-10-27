const APPS_SCRIPT_URL =
    'https://script.google.com/macros/s/AKfycbyB3KZSp7ox3J3fZO_XcRfXzmRp9GVkLKxfPZ3yEas/dev';

const RECAPTCHA_SITE_KEY =
    '6LdUnwIuAAAAAKu2H-GxH3OAFLKxXxOf8a7XpG-f';


let recaptchaWidgetId = null;
let activeClientNonce = null;


window.onRecaptchaLoaded = function () {
    const status = document.getElementById('status');

    status.textContent =
        'Complete the verification to create your ticket.';

    recaptchaWidgetId =
        grecaptcha.enterprise.render(
            'captcha-container',
            {
                sitekey: RECAPTCHA_SITE_KEY,
    
                callback: function (token) {
                    requestTicket(token);
                },
    
                'expired-callback': function () {
                    status.textContent =
                        'Verification expired. Please try again.';
                },
    
                'error-callback': function () {
                    status.textContent =
                        'Verification failed to load.';
                }
            }
        );
};


function requestTicket(captchaToken) {
    const status =
        document.getElementById('status');

    status.textContent =
        'Creating your ticket…';

    activeClientNonce =
        createClientNonce();

    document.getElementById('captcha-token').value =
        captchaToken;

    document.getElementById('client-nonce').value =
        activeClientNonce;

    const form =
        document.getElementById('ticket-init-form');

    form.action = APPS_SCRIPT_URL;
    form.submit();
}


function createClientNonce() {
    const bytes = new Uint8Array(16);

    crypto.getRandomValues(bytes);

    return Array.from(bytes)
        .map(function (value) {
            return value
                .toString(16)
                .padStart(2, '0');
        })
        .join('');
}


window.addEventListener(
    'message',
    function (event) {
        const data = event.data;

        if (
            !data ||
            data.source !== 'ph-bug-report-backend'
        ) {
            return;
        }

        if (
            data.clientNonce &&
            data.clientNonce !== activeClientNonce
        ) {
            return;
        }

        if (data.type === 'ticket-ready') {
            showTicket(data);
            return;
        }

        if (data.type === 'ticket-error') {
            showTicketError(data);
        }
    }
);


function showTicket(data) {
    const status =
        document.getElementById('status');

    status.textContent =
        'Verification complete.';

    const ticketSection =
        document.getElementById('ticket-section');

    document.getElementById('ticket-id').value =
        data.ticketId;

    const dropboxLink =
        document.getElementById('dropbox-link');

    dropboxLink.href =
        data.dropboxUrl;

    ticketSection.hidden = false;

    document.getElementById(
        'captcha-container'
    ).hidden = true;
}


function showTicketError(data) {
    const status =
        document.getElementById('status');

    status.textContent =
        data.message ||
        'Unable to create the ticket.';

    if (
        recaptchaWidgetId !== null &&
        window.grecaptcha
    ) {
        grecaptcha.enterprise.reset(
            recaptchaWidgetId
        );
    }
}


window.addEventListener(
    'DOMContentLoaded',
    function () {
        document
            .getElementById('copy-ticket')
            .addEventListener(
                'click',
                async function () {
                    const value =
                        document.getElementById(
                            'ticket-id'
                        ).value;

                    await navigator.clipboard.writeText(
                        value
                    );

                    this.textContent = 'Copied';

                    const button = this;

                    setTimeout(function () {
                        button.textContent = 'Copy';
                    }, 1500);
                }
            );
    }
);

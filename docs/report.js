const APPS_SCRIPT_URL =
    'https://script.google.com/macros/s/AKfycbzmywqgBU22ywYgnirawBWDAYeLskn9fSG_x-3xBwqn_g8S3iEbQ36m5034m3c6JN0J/exec';

const RECAPTCHA_SITE_KEY =
    '6LdUnwIuAAAAAKu2H-GxH3OAFLKxXxOf8a7XpG-f';

const MOD_MANIFEST_URL =
    'https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/mod-updates.json';


const REPORT_SUBMISSION_ENABLED = false;


const HIDDEN_MOD_GUIDS = [
    'loupito75.HospitalModUpdateTest'
];


let recaptchaWidgetId = null;

let activeClientNonce = null;
let activeSubmitNonce = null;

let ticketReady = false;

let activeTicketId = '';
let activeReservationToken = '';

let modManifest = null;


/* ------------------------------------------------------------------ */
/* reCAPTCHA                                                          */
/* ------------------------------------------------------------------ */

window.onRecaptchaLoaded = function () {
    const status =
        document.getElementById('status');

    if (ticketReady) {
        return;
    }

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
                    if (ticketReady) {
                        return;
                    }

                    stopTicketLoader();

                    document.getElementById(
                        'captcha-container'
                    ).hidden = false;

                    status.hidden = false;

                    status.textContent =
                        'Verification expired. Please try again.';
                },

                'error-callback': function () {
                    if (ticketReady) {
                        return;
                    }

                    stopTicketLoader();

                    document.getElementById(
                        'captcha-container'
                    ).hidden = false;

                    status.hidden = false;

                    status.textContent =
                        'Verification failed to load.';
                }
            }
        );
};


/* ------------------------------------------------------------------ */
/* Ticket creation                                                    */
/* ------------------------------------------------------------------ */

function requestTicket(captchaToken) {
    if (ticketReady) {
        return;
    }

    activeClientNonce =
        createClientNonce();

    document.getElementById(
        'captcha-token'
    ).value = captchaToken;

    document.getElementById(
        'client-nonce'
    ).value = activeClientNonce;

    startTicketLoader();

    const form =
        document.getElementById(
            'ticket-init-form'
        );

    form.action =
        APPS_SCRIPT_URL;

    form.submit();
}


function startTicketLoader() {
    document.getElementById(
        'captcha-container'
    ).hidden = true;

    document.getElementById(
        'status'
    ).hidden = true;

    document.getElementById(
        'ticket-loader'
    ).hidden = false;
}


function stopTicketLoader() {
    document.getElementById(
        'ticket-loader'
    ).hidden = true;
}


function createClientNonce() {
    const bytes =
        new Uint8Array(16);

    crypto.getRandomValues(bytes);

    return Array.from(bytes)
        .map(function (value) {
            return value
                .toString(16)
                .padStart(2, '0');
        })
        .join('');
}


/* ------------------------------------------------------------------ */
/* Backend messages                                                   */
/* ------------------------------------------------------------------ */

window.addEventListener(
    'message',
    function (event) {
        const data =
            event.data;

        if (
            !data ||
            data.source !==
                'ph-bug-report-backend'
        ) {
            return;
        }

        if (
            data.clientNonce &&
            data.clientNonce !== activeClientNonce &&
            data.clientNonce !== activeSubmitNonce
        ) {
            return;
        }

        if (data.type === 'ticket-ready') {
            showTicket(data);
            return;
        }

        if (data.type === 'ticket-error') {
            if (!ticketReady) {
                showTicketError(data);
            }
            else {
                showReportError(data);
            }

            return;
        }

        if (data.type === 'report-submitted') {
            showReportSubmitted(data);
            return;
        }

        if (data.type === 'report-error') {
            showReportError(data);
        }
    }
);


/* ------------------------------------------------------------------ */
/* Ticket UI                                                          */
/* ------------------------------------------------------------------ */

function showTicket(data) {
    ticketReady = true;

    activeTicketId =
        String(data.ticketId || '');

    activeReservationToken =
        String(data.reservationToken || '');

    stopTicketLoader();

    document.getElementById(
        'verification-section'
    ).hidden = true;

    document.getElementById(
        'ticket-id'
    ).value = activeTicketId;

    document.getElementById(
        'dropbox-link'
    ).href =
        String(data.dropboxUrl || '#');

    document.getElementById(
        'ticket-section'
    ).hidden = false;

    document.getElementById(
        'report-section'
    ).hidden = false;

    updateSubmitAvailability();
}


function showTicketError(data) {
    stopTicketLoader();

    const captchaContainer =
        document.getElementById(
            'captcha-container'
        );

    const status =
        document.getElementById(
            'status'
        );

    captchaContainer.hidden = false;
    status.hidden = false;

    status.textContent =
        data.message ||
        'Unable to create the ticket. Please try again.';

    if (
        recaptchaWidgetId !== null &&
        window.grecaptcha
    ) {
        grecaptcha.enterprise.reset(
            recaptchaWidgetId
        );
    }
}


/* ------------------------------------------------------------------ */
/* Mod manifest                                                       */
/* ------------------------------------------------------------------ */

async function loadModManifest() {
    const select =
        document.getElementById(
            'mod-select'
        );

    const status =
        document.getElementById(
            'mod-status'
        );

    try {
        const response =
            await fetch(
                MOD_MANIFEST_URL,
                {
                    cache: 'no-store'
                }
            );

        if (!response.ok) {
            throw new Error(
                'Manifest request failed: ' +
                response.status
            );
        }

        const manifest =
            await response.json();

        if (
            !manifest ||
            !Array.isArray(manifest.mods)
        ) {
            throw new Error(
                'Invalid mod manifest.'
            );
        }

        const visibleMods =
            manifest.mods.filter(
                function (mod) {
                    const guid =
                        String(
                            mod.guid || ''
                        );

                    return !HIDDEN_MOD_GUIDS.includes(
                        guid
                    );
                }
            );

        modManifest = {
            ...manifest,
            mods: visibleMods
        };

        populateModList(
            visibleMods
        );

        status.textContent =
            'Select the mod affected by the problem.';

        updateSubmitAvailability();
    }
    catch (error) {
        console.error(
            'Unable to load mod manifest.',
            error
        );

        select.innerHTML = '';

        const option =
            document.createElement(
                'option'
            );

        option.value = '';

        option.textContent =
            'Unable to load mod list';

        select.appendChild(
            option
        );

        select.disabled = true;

        status.textContent =
            'Unable to load the current mod list. ' +
            'Please refresh the page.';
    }
}


function populateModList(mods) {
    const select =
        document.getElementById(
            'mod-select'
        );

    select.innerHTML = '';

    const placeholder =
        document.createElement(
            'option'
        );

    placeholder.value = '';

    placeholder.textContent =
        'Select a mod…';

    select.appendChild(
        placeholder
    );

    const sortedMods =
        mods.slice().sort(
            function (a, b) {
                return String(a.name)
                    .localeCompare(
                        String(b.name)
                    );
            }
        );

    sortedMods.forEach(
        function (mod) {
            const option =
                document.createElement(
                    'option'
                );

            option.value =
                String(mod.guid || '');

            option.textContent =
                String(
                    mod.name ||
                    mod.guid ||
                    ''
                );

            option.dataset.name =
                String(mod.name || '');

            option.dataset.version =
                String(mod.version || '');

            select.appendChild(
                option
            );
        }
    );

    select.disabled = false;
}


function handleModSelection() {
    const select =
        document.getElementById(
            'mod-select'
        );

    const versionInput =
        document.getElementById(
            'mod-version'
        );

    const latestVersion =
        document.getElementById(
            'latest-version'
        );

    const selectedOption =
        select.options[
            select.selectedIndex
        ];

    if (
        !selectedOption ||
        !selectedOption.value
    ) {
        versionInput.value = '';
        latestVersion.textContent = '';
        return;
    }

    const version =
        selectedOption.dataset.version ||
        '';

    versionInput.value =
        version;

    latestVersion.textContent =
        version
            ? 'Latest published version: ' + version
            : '';
}


/* ------------------------------------------------------------------ */
/* Contact email                                                      */
/* ------------------------------------------------------------------ */

function setupContactEmail() {
    /*
     * Built at runtime instead of writing the complete address
     * directly in the HTML. This is basic protection against
     * simple email-address harvesters.
     */
    const user =
        'loupito75';

    const domain =
        'gmail' + '.' + 'com';

    const address =
        user + '@' + domain;

    const link =
        document.getElementById(
            'contact-email'
        );

    link.textContent =
        address;

    link.href =
        'mailto:' + address;
}


/* ------------------------------------------------------------------ */
/* Report submission                                                  */
/* ------------------------------------------------------------------ */

function updateSubmitAvailability() {
    const button =
        document.getElementById(
            'submit-report'
        );

    const status =
        document.getElementById(
            'report-status'
        );

    if (!REPORT_SUBMISSION_ENABLED) {
        button.disabled = true;

        status.textContent =
            'Report submission will be enabled after the ' +
            'backend submission step is installed.';

        return;
    }

    if (
        !ticketReady ||
        !activeTicketId ||
        !activeReservationToken ||
        !modManifest
    ) {
        button.disabled = true;

        status.textContent =
            'Preparing the report form…';

        return;
    }

    button.disabled = false;

    status.textContent =
        'Complete all required fields and submit your report.';
}


function submitReport(event) {
    event.preventDefault();

    if (!REPORT_SUBMISSION_ENABLED) {
        return;
    }

    const form =
        document.getElementById(
            'bug-report-form'
        );

    if (!form.reportValidity()) {
        return;
    }

    const modSelect =
        document.getElementById(
            'mod-select'
        );

    const selectedOption =
        modSelect.options[
            modSelect.selectedIndex
        ];

    if (
        !selectedOption ||
        !selectedOption.value
    ) {
        document.getElementById(
            'report-status'
        ).textContent =
            'Please select a mod.';

        return;
    }

    activeSubmitNonce =
        createClientNonce();

    setHiddenValue(
        'submit-client-nonce',
        activeSubmitNonce
    );

    setHiddenValue(
        'submit-ticket-id',
        activeTicketId
    );

    setHiddenValue(
        'submit-reservation-token',
        activeReservationToken
    );

    setHiddenValue(
        'submit-reporter-name',
        document.getElementById(
            'reporter-name'
        ).value.trim()
    );

    setHiddenValue(
        'submit-reporter-email',
        document.getElementById(
            'reporter-email'
        ).value.trim()
    );

    setHiddenValue(
        'submit-mod-guid',
        selectedOption.value
    );

    setHiddenValue(
        'submit-mod-name',
        selectedOption.dataset.name || ''
    );

    setHiddenValue(
        'submit-mod-version',
        document.getElementById(
            'mod-version'
        ).value.trim()
    );

    setHiddenValue(
        'submit-game-version',
        document.getElementById(
            'game-version'
        ).value.trim()
    );

    setHiddenValue(
        'submit-description',
        document.getElementById(
            'description'
        ).value.trim()
    );

    setHiddenValue(
        'submit-steps',
        document.getElementById(
            'steps'
        ).value.trim()
    );

    setHiddenValue(
        'submit-expected',
        document.getElementById(
            'expected'
        ).value.trim()
    );

    setHiddenValue(
        'submit-additional-notes',
        document.getElementById(
            'additional-notes'
        ).value.trim()
    );

    const submitButton =
        document.getElementById(
            'submit-report'
        );

    submitButton.disabled = true;

    document.getElementById(
        'report-status'
    ).textContent =
        'Sending your report…';

    const bridgeForm =
        document.getElementById(
            'report-submit-form'
        );

    bridgeForm.action =
        APPS_SCRIPT_URL;

    bridgeForm.submit();
}


function setHiddenValue(id, value) {
    document.getElementById(
        id
    ).value = value;
}


function showReportSubmitted() {
    document.getElementById(
        'submit-report'
    ).disabled = true;

    document.getElementById(
        'report-status'
    ).textContent =
        'Report submitted successfully. Thank you.';

    document.getElementById(
        'bug-report-form'
    ).querySelectorAll(
        'input, select, textarea'
    ).forEach(
        function (element) {
            element.disabled = true;
        }
    );
}


function showReportError(data) {
    document.getElementById(
        'report-status'
    ).textContent =
        data.message ||
        'Unable to submit the report. Please try again.';

    activeSubmitNonce = null;

    updateSubmitAvailability();
}


/* ------------------------------------------------------------------ */
/* Ticket copy                                                        */
/* ------------------------------------------------------------------ */

async function copyTicketNumber(button) {
    const value =
        document.getElementById(
            'ticket-id'
        ).value;

    try {
        await navigator.clipboard.writeText(
            value
        );

        button.textContent =
            'Copied';

        setTimeout(
            function () {
                button.textContent =
                    'Copy';
            },
            1500
        );
    }
    catch (error) {
        console.error(
            'Unable to copy ticket number.',
            error
        );
    }
}


/* ------------------------------------------------------------------ */
/* Startup                                                            */
/* ------------------------------------------------------------------ */

window.addEventListener(
    'DOMContentLoaded',
    function () {
        document.getElementById(
            'copy-ticket'
        ).addEventListener(
            'click',
            function () {
                copyTicketNumber(this);
            }
        );

        document.getElementById(
            'mod-select'
        ).addEventListener(
            'change',
            handleModSelection
        );

        document.getElementById(
            'bug-report-form'
        ).addEventListener(
            'submit',
            submitReport
        );

        setupContactEmail();

        loadModManifest();
    }
);

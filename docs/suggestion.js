const APPS_SCRIPT_URL =
    'https://script.google.com/macros/s/AKfycbzmywqgBU22ywYgnirawBWDAYeLskn9fSG_x-3xBwqn_g8S3iEbQ36m5034m3c6JN0J/exec';

const RECAPTCHA_SITE_KEY =
    '6LdUnwIuAAAAAKu2H-GxH3OAFLKxXxOf8a7XpG-f';

const MOD_MANIFEST_URL =
    'https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/catalog/mod-catalog.json';


const SUGGESTION_SUBMISSION_ENABLED = true;


const HIDDEN_MOD_GUIDS = [
    'loupito75.HospitalModUpdateTest'
];


let recaptchaWidgetId = null;

let activeClientNonce = null;
let activeSubmitNonce = null;

let ticketReady = false;

let activeTicketId = '';
let activeReservationToken = '';

let modManifestLoaded = false;
let modManifestAvailable = false;


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
                showSuggestionError(data);
            }

            return;
        }

        if (data.type === 'report-submitted') {
            showSuggestionSubmitted(data);
            return;
        }

        if (data.type === 'report-error') {
            showSuggestionError(data);
        }
    }
);


/* ------------------------------------------------------------------ */
/* Ticket UI                                                          */
/* ------------------------------------------------------------------ */

function showTicket(data) {
    const ticketId =
        String(data.ticketId || '');

    if (
        data.requestType !== 'suggestion' ||
        !/^PHS-[0-9A-Z]{8}$/.test(ticketId)
    ) {
        showTicketError({
            message:
                'The backend did not create a valid suggestion ticket.'
        });

        return;
    }

    ticketReady = true;

    activeTicketId =
        ticketId;

    activeReservationToken =
        String(data.reservationToken || '');

    if (!activeReservationToken) {
        ticketReady = false;

        showTicketError({
            message:
                'The suggestion ticket reservation is incomplete.'
        });

        return;
    }

    stopTicketLoader();

    document.getElementById(
        'verification-section'
    ).hidden = true;

    document.getElementById(
        'ticket-id'
    ).value = activeTicketId;

    document.getElementById(
        'ticket-section'
    ).hidden = false;

    document.getElementById(
        'suggestion-section'
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
        'Unable to create the suggestion ticket. Please try again.';

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

        populateModList(
            visibleMods
        );

        modManifestLoaded = true;
        modManifestAvailable = true;

        status.textContent =
            'Select the published mod this suggestion concerns.';

        updateSubmitAvailability();
    }
    catch (error) {
        console.error(
            'Unable to load mod manifest.',
            error
        );

        modManifestLoaded = true;
        modManifestAvailable = false;

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
            'New mod and General / Other suggestions are still available.';

        updateSubmitAvailability();
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

            select.appendChild(
                option
            );
        }
    );

    select.disabled = false;
}


/* ------------------------------------------------------------------ */
/* Suggestion target                                                  */
/* ------------------------------------------------------------------ */

function handleSuggestionTarget() {
    const target =
        document.getElementById(
            'suggestion-target'
        ).value;

    const modField =
        document.getElementById(
            'mod-field'
        );

    const modSelect =
        document.getElementById(
            'mod-select'
        );

    const existingMod =
        target === 'existing-mod';

    modField.hidden =
        !existingMod;

    modSelect.required =
        existingMod;

    if (!existingMod) {
        modSelect.value = '';
    }

    updateSubmitAvailability();
}


/* ------------------------------------------------------------------ */
/* Suggestion submission                                              */
/* ------------------------------------------------------------------ */

function updateSubmitAvailability() {
    const button =
        document.getElementById(
            'submit-suggestion'
        );

    const status =
        document.getElementById(
            'suggestion-status'
        );

    if (!SUGGESTION_SUBMISSION_ENABLED) {
        button.disabled = true;

        status.textContent =
            'Suggestion submission is temporarily unavailable.';

        return;
    }

    if (
        !ticketReady ||
        !activeTicketId ||
        !activeReservationToken
    ) {
        button.disabled = true;

        status.textContent =
            'Preparing the suggestion form…';

        return;
    }

    const target =
        document.getElementById(
            'suggestion-target'
        ).value;

    if (
        target === 'existing-mod' &&
        (
            !modManifestLoaded ||
            !modManifestAvailable
        )
    ) {
        button.disabled = true;

        status.textContent =
            'The mod list is unavailable. Choose New mod idea or General / Other, or refresh the page.';

        return;
    }

    button.disabled = false;

    status.textContent =
        'Complete all required fields and submit your suggestion.';
}


function startSuggestionLoader() {
    const button =
        document.getElementById(
            'submit-suggestion'
        );

    const status =
        document.getElementById(
            'suggestion-status'
        );

    const loader =
        document.getElementById(
            'suggestion-loader'
        );

    button.disabled = true;
    button.hidden = true;

    status.hidden = true;
    loader.hidden = false;
}


function stopSuggestionLoader() {
    document.getElementById(
        'suggestion-loader'
    ).hidden = true;

    document.getElementById(
        'suggestion-status'
    ).hidden = false;
}


function submitSuggestion(event) {
    event.preventDefault();

    if (!SUGGESTION_SUBMISSION_ENABLED) {
        return;
    }

    const form =
        document.getElementById(
            'suggestion-form'
        );

    if (!form.reportValidity()) {
        return;
    }

    const target =
        document.getElementById(
            'suggestion-target'
        ).value;

    let modGuid = '';
    let modName = '';

    if (target === 'existing-mod') {
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
                'suggestion-status'
            ).textContent =
                'Please select a mod.';

            return;
        }

        modGuid =
            selectedOption.value;

        modName =
            selectedOption.dataset.name || '';
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
        'submit-suggestion-target',
        target
    );

    setHiddenValue(
        'submit-mod-guid',
        modGuid
    );

    setHiddenValue(
        'submit-mod-name',
        modName
    );

    setHiddenValue(
        'submit-title',
        document.getElementById(
            'suggestion-title'
        ).value.trim()
    );

    setHiddenValue(
        'submit-problem',
        document.getElementById(
            'problem'
        ).value.trim()
    );

    setHiddenValue(
        'submit-suggested-behaviour',
        document.getElementById(
            'suggested-behaviour'
        ).value.trim()
    );

    setHiddenValue(
        'submit-example-use-case',
        document.getElementById(
            'example-use-case'
        ).value.trim()
    );

    setHiddenValue(
        'submit-usefulness',
        document.getElementById(
            'usefulness'
        ).value.trim()
    );

    setHiddenValue(
        'submit-workaround',
        document.getElementById(
            'workaround'
        ).value.trim()
    );

    setHiddenValue(
        'submit-additional-notes',
        document.getElementById(
            'additional-notes'
        ).value.trim()
    );

    startSuggestionLoader();

    const bridgeForm =
        document.getElementById(
            'suggestion-submit-form'
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


function showSuggestionSubmitted(data) {
    stopSuggestionLoader();

    const button =
        document.getElementById(
            'submit-suggestion'
        );

    button.disabled = true;
    button.hidden = true;

    const status =
        document.getElementById(
            'suggestion-status'
        );

    if (data.alreadySubmitted === true) {
        status.textContent =
            'This suggestion was already received. No duplicate was sent.';
    }
    else {
        status.textContent =
            'Suggestion submitted successfully. Thank you.';
    }

    const confirmationStatus =
        document.getElementById(
            'confirmation-status'
        );

    if (data.confirmationEmailSent === true) {
        confirmationStatus.className =
            'confirmation-note confirmation-success';

        confirmationStatus.textContent =
            'Your suggestion was submitted successfully. ' +
            'A confirmation email containing ticket ' +
            activeTicketId +
            ' was sent to the email address you provided.';
    }
    else {
        confirmationStatus.className =
            'confirmation-note confirmation-warning';

        confirmationStatus.textContent =
            'Your suggestion was submitted successfully, but the ' +
            'confirmation email could not be sent. Please copy ' +
            'and keep ticket ' +
            activeTicketId +
            '. Your suggestion was still received.';
    }

    document.getElementById(
        'suggestion-form'
    ).querySelectorAll(
        'input, select, textarea'
    ).forEach(
        function (element) {
            element.disabled = true;
        }
    );

    document.getElementById(
        'ticket-section'
    ).scrollIntoView({
        behavior: 'smooth',
        block: 'start'
    });
}


function showSuggestionError(data) {
    stopSuggestionLoader();

    const status =
        document.getElementById(
            'suggestion-status'
        );

    status.textContent =
        data.message ||
        'Unable to submit the suggestion. Please try again.';

    activeSubmitNonce = null;

    if (
        SUGGESTION_SUBMISSION_ENABLED &&
        ticketReady &&
        activeTicketId &&
        activeReservationToken
    ) {
        const target =
            document.getElementById(
                'suggestion-target'
            ).value;

        const blockedByManifest =
            target === 'existing-mod' &&
            (
                !modManifestLoaded ||
                !modManifestAvailable
            );

        if (!blockedByManifest) {
            const button =
                document.getElementById(
                    'submit-suggestion'
                );

            button.hidden = false;
            button.disabled = false;
        }
    }
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
                copyTicketNumber(
                    this
                );
            }
        );

        document.getElementById(
            'suggestion-target'
        ).addEventListener(
            'change',
            handleSuggestionTarget
        );

        document.getElementById(
            'suggestion-form'
        ).addEventListener(
            'submit',
            submitSuggestion
        );

        loadModManifest();
    }
);

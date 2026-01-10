// Exchange Calendar Sync - Settings Page JavaScript

let mappingsCount = 0;

/**
 * Load settings from the API
 */
async function loadSettings() {
    try {
        document.getElementById('loading').style.display = 'block';
        document.getElementById('settings-form').style.display = 'none';

        const response = await fetch('/api/settings');
        if (!response.ok) {
            throw new Error('Failed to load settings');
        }

        const settings = await response.json();

        // Sync settings
        document.getElementById('syncInterval').value = settings.sync?.syncIntervalMinutes || 5;
        document.getElementById('lookbackDays').value = settings.sync?.lookbackDays || 30;
        document.getElementById('lookForwardDays').value = settings.sync?.lookForwardDays || 30;

        // Exchange On-Premise settings
        document.getElementById('onPremServerUrl').value = settings.exchangeOnPremise?.serverUrl || '';
        document.getElementById('onPremDomain').value = settings.exchangeOnPremise?.domain || '';
        document.getElementById('onPremUsername').value = settings.exchangeOnPremise?.username || '';
        document.getElementById('onPremPassword').value = '';
        document.getElementById('onPremPassword').placeholder = settings.exchangeOnPremise?.serverUrl ? 'Leave blank to keep existing' : 'Enter password';

        // Exchange Online Source settings
        document.getElementById('onlineSourceTenantId').value = settings.exchangeOnlineSource?.tenantId || '';
        document.getElementById('onlineSourceClientId').value = settings.exchangeOnlineSource?.clientId || '';
        document.getElementById('onlineSourceClientSecret').value = '';
        document.getElementById('onlineSourceClientSecret').placeholder = settings.exchangeOnlineSource?.clientId ? 'Leave blank to keep existing' : 'Enter client secret';

        // Exchange Online (destination) settings
        document.getElementById('onlineTenantId').value = settings.exchangeOnline?.tenantId || '';
        document.getElementById('onlineClientId').value = settings.exchangeOnline?.clientId || '';
        document.getElementById('onlineClientSecret').value = '';
        document.getElementById('onlineClientSecret').placeholder = settings.exchangeOnline?.clientId ? 'Leave blank to keep existing' : 'Enter client secret';

        // Persistence settings
        document.getElementById('dataPath').value = settings.persistence?.dataPath || './data';
        document.getElementById('enableStatePersistence').checked = settings.persistence?.enableStatePersistence ?? true;

        // OpenTelemetry settings
        document.getElementById('otelEnabled').checked = settings.openTelemetry?.enabled ?? false;
        document.getElementById('otelEndpoint').value = settings.openTelemetry?.endpoint || 'http://localhost:4317';
        document.getElementById('otelServiceName').value = settings.openTelemetry?.serviceName || 'exchange-calendar-sync';
        document.getElementById('otelEnvironment').value = settings.openTelemetry?.environment || 'production';
        document.getElementById('otelExportLogs').checked = settings.openTelemetry?.exportLogs ?? true;
        document.getElementById('otelExportMetrics').checked = settings.openTelemetry?.exportMetrics ?? true;
        document.getElementById('otelProtocol').value = settings.openTelemetry?.protocol || 'grpc';
        // Display actual headers value from API
        document.getElementById('otelHeaders').value = settings.openTelemetry?.headers || '';
        document.getElementById('otelHeaders').placeholder = 'signoz-ingestion-key=xxx';
        document.getElementById('otelMetricsInterval').value = settings.openTelemetry?.metricsExportIntervalSeconds || 60;
        toggleOtelSettings();

        // Load mailbox mappings
        const container = document.getElementById('mappings-container');
        container.innerHTML = '';
        mappingsCount = 0;

        if (settings.exchangeOnPremise?.mailboxMappings && settings.exchangeOnPremise.mailboxMappings.length > 0) {
            settings.exchangeOnPremise.mailboxMappings.forEach(mapping => {
                addMapping(mapping.name, mapping.sourceMailbox, mapping.destinationMailbox, mapping.sourceType);
            });
        }

        document.getElementById('loading').style.display = 'none';
        document.getElementById('settings-form').style.display = 'block';

    } catch (error) {
        console.error('Failed to load settings:', error);
        showMessage('Failed to load settings: ' + error.message, 'error');
        document.getElementById('loading').innerHTML = '<p class="error-message">Failed to load settings. Please refresh the page.</p>';
    }
}

/**
 * Add a mailbox mapping to the form
 * @param {string} name - Mapping name
 * @param {string} sourceMailbox - Source mailbox email
 * @param {string} destinationMailbox - Destination mailbox email
 * @param {string} sourceType - Source type (ExchangeOnPremise or ExchangeOnline)
 */
function addMapping(name = '', sourceMailbox = '', destinationMailbox = '', sourceType = 'ExchangeOnline') {
    mappingsCount++;
    const container = document.getElementById('mappings-container');

    const mappingDiv = document.createElement('div');
    mappingDiv.className = 'mapping-item';
    mappingDiv.id = `mapping-${mappingsCount}`;

    mappingDiv.innerHTML = `
        <div class="mapping-header">
            <span class="mapping-number">Mapping #${mappingsCount}</span>
            <button type="button" class="btn btn-danger" onclick="removeMapping(${mappingsCount})">Remove</button>
        </div>
        <div class="form-group" style="margin-bottom: 15px;">
            <label>Name (for logs/filtering)</label>
            <input type="text" class="mapping-name" value="${escapeHtml(name)}" placeholder="e.g., Alpha to Beta">
        </div>
        <div class="mapping-fields">
            <div class="form-group">
                <label>Source Mailbox</label>
                <input type="text" class="mapping-source" value="${escapeHtml(sourceMailbox)}" placeholder="source@domain.com">
            </div>
            <div class="form-group">
                <label>Destination Mailbox</label>
                <input type="text" class="mapping-destination" value="${escapeHtml(destinationMailbox)}" placeholder="destination@domain.com">
            </div>
            <div class="form-group">
                <label>Source Type</label>
                <select class="mapping-type">
                    <option value="ExchangeOnPremise" ${sourceType === 'ExchangeOnPremise' ? 'selected' : ''}>Exchange On-Premise</option>
                    <option value="ExchangeOnline" ${sourceType === 'ExchangeOnline' ? 'selected' : ''}>Exchange Online</option>
                </select>
            </div>
        </div>
    `;

    container.appendChild(mappingDiv);
}

/**
 * Remove a mailbox mapping from the form
 * @param {number} id - The mapping ID to remove
 */
function removeMapping(id) {
    const element = document.getElementById(`mapping-${id}`);
    if (element) {
        element.remove();
    }
}

/**
 * Get all mailbox mappings from the form
 * @returns {Array} Array of mapping objects
 */
function getMappings() {
    const mappings = [];
    const items = document.querySelectorAll('.mapping-item');

    items.forEach(item => {
        const name = item.querySelector('.mapping-name').value.trim();
        const source = item.querySelector('.mapping-source').value.trim();
        const destination = item.querySelector('.mapping-destination').value.trim();
        const type = item.querySelector('.mapping-type').value;

        if (source && destination) {
            mappings.push({
                name: name,
                sourceMailbox: source,
                destinationMailbox: destination,
                sourceType: type
            });
        }
    });

    return mappings;
}

/**
 * Save settings to the API
 * @param {Event} e - Form submit event
 */
async function saveSettings(e) {
    e.preventDefault();

    try {
        const settings = {
            sync: {
                syncIntervalMinutes: parseInt(document.getElementById('syncInterval').value) || 5,
                lookbackDays: parseInt(document.getElementById('lookbackDays').value) || 30,
                lookForwardDays: parseInt(document.getElementById('lookForwardDays').value) || 30
            },
            exchangeOnPremise: {
                serverUrl: document.getElementById('onPremServerUrl').value,
                domain: document.getElementById('onPremDomain').value,
                username: document.getElementById('onPremUsername').value,
                password: document.getElementById('onPremPassword').value || null,
                mailboxMappings: getMappings()
            },
            exchangeOnlineSource: {
                tenantId: document.getElementById('onlineSourceTenantId').value,
                clientId: document.getElementById('onlineSourceClientId').value,
                clientSecret: document.getElementById('onlineSourceClientSecret').value || null
            },
            exchangeOnline: {
                tenantId: document.getElementById('onlineTenantId').value,
                clientId: document.getElementById('onlineClientId').value,
                clientSecret: document.getElementById('onlineClientSecret').value || null
            },
            persistence: {
                dataPath: document.getElementById('dataPath').value || './data',
                enableStatePersistence: document.getElementById('enableStatePersistence').checked
            },
            openTelemetry: {
                enabled: document.getElementById('otelEnabled').checked,
                endpoint: document.getElementById('otelEndpoint').value || 'http://localhost:4317',
                serviceName: document.getElementById('otelServiceName').value || 'exchange-calendar-sync',
                environment: document.getElementById('otelEnvironment').value || 'production',
                exportLogs: document.getElementById('otelExportLogs').checked,
                exportMetrics: document.getElementById('otelExportMetrics').checked,
                protocol: document.getElementById('otelProtocol').value || 'grpc',
                headers: document.getElementById('otelHeaders').value || null,
                metricsExportIntervalSeconds: parseInt(document.getElementById('otelMetricsInterval').value) || 60
            }
        };

        const response = await fetch('/api/settings', {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(settings)
        });

        const result = await response.json();

        if (response.ok) {
            showMessage(result.message + ' The page will reload shortly.', 'success', 0);
            // Wait for the app to restart and then reload the page
            setTimeout(() => {
                waitForServerAndReload();
            }, 2000);
        } else {
            showMessage(result.message || 'Failed to save settings', 'error');
        }
    } catch (error) {
        console.error('Failed to save settings:', error);
        showMessage('Failed to save settings: ' + error.message, 'error');
    }
}

/**
 * Toggle OpenTelemetry settings visibility
 */
function toggleOtelSettings() {
    const enabled = document.getElementById('otelEnabled').checked;
    const settingsDiv = document.getElementById('otel-settings');
    settingsDiv.style.display = enabled ? 'block' : 'none';
}

/**
 * Initialize the settings page
 */
function initSettings() {
    document.getElementById('settings-form').addEventListener('submit', saveSettings);
    document.getElementById('otelEnabled').addEventListener('change', toggleOtelSettings);
    loadSettings();
}

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSettings);
} else {
    initSettings();
}

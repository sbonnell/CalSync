// Exchange Calendar Sync - Dashboard JavaScript

let autoRefreshInterval;
let countdownInterval;
let selectedMailbox = null;
let nextSyncTime = null;

/**
 * Update the countdown timer display
 */
function updateCountdown() {
    const statusEl = document.getElementById('sync-status');
    if (!nextSyncTime) {
        return;
    }

    const now = new Date();
    const diff = nextSyncTime - now;

    if (diff <= 0) {
        statusEl.textContent = 'SYNCING SOON...';
        return;
    }

    const minutes = Math.floor(diff / 60000);
    const seconds = Math.floor((diff % 60000) / 1000);

    if (minutes > 0) {
        statusEl.textContent = `NEXT: ${minutes}m ${seconds}s`;
    } else {
        statusEl.textContent = `NEXT: ${seconds}s`;
    }
}

/**
 * Fetch and display the current sync status
 */
async function fetchStatus() {
    try {
        const response = await fetch('/api/sync/status');
        const data = await response.json();

        // Update status badge with countdown timer when idle
        const statusEl = document.getElementById('sync-status');
        const toggleBtn = document.getElementById('toggle-sync-btn');

        if (data.isRunning) {
            statusEl.textContent = 'RUNNING';
            statusEl.className = 'status-badge status-running pulse';
            nextSyncTime = null;
        } else if (!data.isSyncEnabled) {
            statusEl.textContent = 'STOPPED';
            statusEl.className = 'status-badge status-stopped';
            nextSyncTime = null;
        } else if (data.nextScheduledSync && new Date(data.nextScheduledSync) > new Date(2000, 0, 1)) {
            nextSyncTime = new Date(data.nextScheduledSync);
            updateCountdown();
            statusEl.className = 'status-badge status-idle';
        } else {
            statusEl.textContent = 'IDLE';
            statusEl.className = 'status-badge status-idle';
            nextSyncTime = null;
        }

        // Update toggle button state
        if (data.isSyncEnabled) {
            toggleBtn.textContent = 'Stop Sync';
            toggleBtn.style.background = 'linear-gradient(135deg, #ef4444 0%, #dc2626 100%)';
        } else {
            toggleBtn.textContent = 'Start Sync';
            toggleBtn.style.background = 'linear-gradient(135deg, #10b981 0%, #059669 100%)';
        }

        // Update statistics
        document.getElementById('total-synced').textContent = data.totalItemsSynced || 0;
        document.getElementById('total-errors').textContent = data.totalErrors || 0;

        // Update last sync time
        if (data.lastSyncTime) {
            const lastSync = new Date(data.lastSyncTime);
            document.getElementById('last-sync').textContent = formatRelativeTime(lastSync);
        } else {
            document.getElementById('last-sync').textContent = 'Never';
        }

        // Update next scheduled sync
        if (!data.isSyncEnabled) {
            document.getElementById('next-sync').textContent = 'Scheduled sync is disabled';
            document.getElementById('next-sync').style.color = '#ef4444';
        } else if (data.nextScheduledSync && new Date(data.nextScheduledSync) > new Date(2000, 0, 1)) {
            const nextSync = new Date(data.nextScheduledSync);
            document.getElementById('next-sync').textContent = `Next scheduled sync: ${formatRelativeTime(nextSync)}`;
            document.getElementById('next-sync').style.color = '#666';
        } else {
            document.getElementById('next-sync').textContent = '';
            document.getElementById('next-sync').style.color = '#666';
        }

        // Update mailbox list
        const mailboxList = document.getElementById('mailbox-list');
        if (data.mailboxStatuses && Object.keys(data.mailboxStatuses).length > 0) {
            mailboxList.innerHTML = Object.entries(data.mailboxStatuses).map(([email, status]) => `
                <li class="mailbox-item ${selectedMailbox === email ? 'selected' : ''}" onclick="filterByMailbox('${escapeHtml(email)}')" title="Click to filter logs by this mailbox">
                    <div class="mailbox-email">${escapeHtml(email)}</div>
                    <div class="mailbox-stats">
                        <span>Status: <strong>${status.status}</strong></span>
                        <span>Evaluated: <strong>${status.itemsEvaluated || 0}</strong></span>
                        <span>Created: <strong>${status.itemsCreated || 0}</strong></span>
                        <span>Updated: <strong>${status.itemsUpdated || 0}</strong></span>
                        <span>Deleted: <strong>${status.itemsDeleted || 0}</strong></span>
                        <span>Unchanged: <strong>${status.itemsUnchanged || 0}</strong></span>
                        <span>Errors: <strong>${status.errors}</strong></span>
                        ${status.lastSyncTime ? `<span>Last Sync: ${formatRelativeTime(new Date(status.lastSyncTime))}</span>` : ''}
                    </div>
                </li>
            `).join('');
        } else {
            mailboxList.innerHTML = '<li class="loading">No mailbox data available yet</li>';
        }

        // Update button states
        document.getElementById('full-sync-btn').disabled = data.isRunning;
        document.getElementById('incremental-sync-btn').disabled = data.isRunning;

    } catch (error) {
        console.error('Failed to fetch status:', error);
        showMessage('Failed to fetch status: ' + error.message, 'error');
    }
}

/**
 * Start a full sync operation
 */
async function startFullSync() {
    try {
        showMessage('Starting full sync...', 'success');
        const response = await fetch('/api/sync/start?fullSync=true', { method: 'POST' });
        const data = await response.json();

        if (response.ok) {
            showMessage(data.message, 'success');
            setTimeout(fetchStatus, 500);
        } else {
            showMessage(data.message, 'error');
        }
    } catch (error) {
        console.error('Failed to start full sync:', error);
        showMessage('Failed to start full sync: ' + error.message, 'error');
    }
}

/**
 * Start an incremental sync operation
 */
async function startIncrementalSync() {
    try {
        showMessage('Starting incremental sync...', 'success');
        const response = await fetch('/api/sync/start?fullSync=false', { method: 'POST' });
        const data = await response.json();

        if (response.ok) {
            showMessage(data.message, 'success');
            setTimeout(fetchStatus, 500);
        } else {
            showMessage(data.message, 'error');
        }
    } catch (error) {
        console.error('Failed to start incremental sync:', error);
        showMessage('Failed to start incremental sync: ' + error.message, 'error');
    }
}

/**
 * Toggle sync enabled/disabled
 */
async function toggleSync() {
    try {
        const response = await fetch('/api/sync/toggle', { method: 'POST' });
        const data = await response.json();

        if (response.ok) {
            showMessage(data.message, 'success');
            setTimeout(fetchStatus, 100);
        } else {
            showMessage(data.message || 'Failed to toggle sync', 'error');
        }
    } catch (error) {
        console.error('Failed to toggle sync:', error);
        showMessage('Failed to toggle sync: ' + error.message, 'error');
    }
}

/**
 * Restart the application
 */
async function restartApp() {
    if (!confirm('Are you sure you want to restart the application? This will reload the configuration.')) {
        return;
    }

    try {
        showMessage('Restarting application...', 'success');
        const response = await fetch('/api/sync/restart', { method: 'POST' });
        const data = await response.json();

        if (response.ok) {
            showMessage(data.message + ' The page will reload in a few seconds.', 'success');
            clearInterval(autoRefreshInterval);
            setTimeout(() => {
                window.location.reload();
            }, 3000);
        } else {
            showMessage(data.message, 'error');
        }
    } catch (error) {
        console.error('Failed to restart:', error);
        showMessage('Failed to restart: ' + error.message, 'error');
    }
}

/**
 * Refresh the logs display
 */
async function refreshLogs() {
    try {
        const level = document.getElementById('log-level').value;
        const url = level ? `/api/logs?level=${level}&limit=200` : '/api/logs?limit=200';
        const response = await fetch(url);
        let logs = await response.json();

        // Filter by selected mailbox if one is selected
        if (selectedMailbox && logs) {
            const filterTerm = `[${selectedMailbox}]`.toLowerCase();
            logs = logs.filter(log => {
                if (!log.message) return false;
                return log.message.toLowerCase().includes(filterTerm);
            });
        }

        // Update filter badge
        updateFilterBadge();

        const logsContainer = document.getElementById('logs-container');
        if (logs && logs.length > 0) {
            logsContainer.innerHTML = logs.map(log => `
                <div class="log-entry">
                    <span class="log-timestamp">${new Date(log.timestamp).toLocaleString()}</span>
                    <span class="log-level log-level-${log.logLevel.toLowerCase()}">${log.logLevel.toUpperCase()}</span>
                    <div class="log-content">
                        <div class="log-message">${escapeHtml(log.message)}</div>
                        ${log.exception ? `<div class="log-exception">${escapeHtml(log.exception)}</div>` : ''}
                    </div>
                </div>
            `).join('');
        } else {
            logsContainer.innerHTML = selectedMailbox
                ? '<div class="loading">No logs found for this mailbox</div>'
                : '<div class="loading">No logs available</div>';
        }
    } catch (error) {
        console.error('Failed to fetch logs:', error);
        document.getElementById('logs-container').innerHTML = `<div class="error-message">Failed to fetch logs: ${error.message}</div>`;
    }
}

/**
 * Filter logs by mailbox
 * @param {string} email - The mailbox email to filter by
 */
function filterByMailbox(email) {
    if (selectedMailbox === email) {
        // Clicking same mailbox clears filter
        selectedMailbox = null;
    } else {
        selectedMailbox = email;
    }
    // Re-render mailbox list to update selection state
    fetchStatus();
    refreshLogs();
}

/**
 * Clear the mailbox filter
 */
function clearMailboxFilter() {
    selectedMailbox = null;
    fetchStatus();
    refreshLogs();
}

/**
 * Update the filter badge display
 */
function updateFilterBadge() {
    const badge = document.getElementById('mailbox-filter-badge');
    if (selectedMailbox) {
        badge.innerHTML = `
            <span class="filter-badge">
                Filtered: ${escapeHtml(selectedMailbox)}
                <button class="clear-filter" onclick="clearMailboxFilter()" title="Clear filter">&times;</button>
            </span>
        `;
    } else {
        badge.innerHTML = '';
    }
}

/**
 * Initialize the dashboard
 */
function initDashboard() {
    fetchStatus();
    refreshLogs();

    // Update countdown every second
    countdownInterval = setInterval(updateCountdown, 1000);

    // Auto-refresh every 5 seconds
    autoRefreshInterval = setInterval(() => {
        fetchStatus();
        refreshLogs();
    }, 5000);
}

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initDashboard);
} else {
    initDashboard();
}

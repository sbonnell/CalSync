// Exchange Calendar Sync - Common JavaScript Utilities

/**
 * Escape HTML entities in a string to prevent XSS
 * @param {string} text - The text to escape
 * @returns {string} The escaped text
 */
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text || '';
    return div.innerHTML;
}

/**
 * Format a date as a relative time string (e.g., "5 minutes ago")
 * @param {Date} date - The date to format
 * @returns {string} The formatted relative time
 */
function formatRelativeTime(date) {
    const now = new Date();
    const diffMs = now - date;
    const diffSec = Math.floor(diffMs / 1000);
    const diffMin = Math.floor(diffSec / 60);
    const diffHour = Math.floor(diffMin / 60);
    const diffDay = Math.floor(diffHour / 24);

    if (diffSec < 0) {
        // Future time
        const absDiffSec = Math.abs(diffSec);
        const absDiffMin = Math.floor(absDiffSec / 60);
        const absDiffHour = Math.floor(absDiffMin / 60);

        if (absDiffSec < 60) return `in ${absDiffSec} second${absDiffSec > 1 ? 's' : ''}`;
        if (absDiffMin < 60) return `in ${absDiffMin} minute${absDiffMin > 1 ? 's' : ''}`;
        if (absDiffHour < 24) return `in ${absDiffHour} hour${absDiffHour > 1 ? 's' : ''}`;
        return date.toLocaleString();
    }

    if (diffSec < 60) return 'Just now';
    if (diffMin < 60) return `${diffMin} minute${diffMin > 1 ? 's' : ''} ago`;
    if (diffHour < 24) return `${diffHour} hour${diffHour > 1 ? 's' : ''} ago`;
    if (diffDay < 7) return `${diffDay} day${diffDay > 1 ? 's' : ''} ago`;
    return date.toLocaleString();
}

/**
 * Show a message in the message area
 * @param {string} message - The message to display
 * @param {string} type - The type of message ('success', 'error', 'warning')
 * @param {number} duration - How long to show the message in ms (0 for permanent)
 */
function showMessage(message, type, duration = 5000) {
    const messageArea = document.getElementById('message-area');
    if (!messageArea) return;

    const className = type === 'error' ? 'error-message' :
                     type === 'warning' ? 'warning-message' : 'success-message';
    messageArea.innerHTML = `<div class="${className}">${message}</div>`;

    // Scroll to top to show message
    window.scrollTo({ top: 0, behavior: 'smooth' });

    if (duration > 0) {
        setTimeout(() => {
            messageArea.innerHTML = '';
        }, duration);
    }
}

/**
 * Toggle password field visibility
 * @param {string} fieldId - The ID of the password field
 */
function togglePassword(fieldId) {
    const field = document.getElementById(fieldId);
    if (!field) return;

    const button = field.nextElementSibling;
    if (field.type === 'password') {
        field.type = 'text';
        if (button) button.textContent = 'Hide';
    } else {
        field.type = 'password';
        if (button) button.textContent = 'Show';
    }
}

/**
 * Wait for the server to become available and reload the page
 * @param {number} maxAttempts - Maximum number of retry attempts
 * @param {number} interval - Interval between retries in ms
 */
async function waitForServerAndReload(maxAttempts = 30, interval = 1000) {
    showMessage('Waiting for application to restart...', 'warning', 0);

    for (let i = 0; i < maxAttempts; i++) {
        try {
            const response = await fetch('/health', { method: 'GET' });
            if (response.ok) {
                showMessage('Application restarted successfully. Reloading...', 'success', 0);
                setTimeout(() => window.location.reload(), 500);
                return;
            }
        } catch (e) {
            // Server not ready yet, continue waiting
        }
        await new Promise(resolve => setTimeout(resolve, interval));
    }

    showMessage('Application restart is taking longer than expected. Please refresh the page manually.', 'warning', 0);
}

/**
 * Make an API request with error handling
 * @param {string} url - The URL to fetch
 * @param {object} options - Fetch options
 * @returns {Promise<object>} The response data
 */
async function apiRequest(url, options = {}) {
    try {
        const response = await fetch(url, options);
        const data = await response.json();

        if (!response.ok) {
            throw new Error(data.message || `HTTP error ${response.status}`);
        }

        return { success: true, data, status: response.status };
    } catch (error) {
        return { success: false, error: error.message };
    }
}

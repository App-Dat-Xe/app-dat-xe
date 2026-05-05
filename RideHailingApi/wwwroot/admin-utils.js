// Admin Dashboard - Advanced Features & Utilities

// Toast Notification System
class ToastNotification {
    constructor() {
        this.container = this.createContainer();
    }

    createContainer() {
        let container = document.getElementById('toast-container');
        if (!container) {
            container = document.createElement('div');
            container.id = 'toast-container';
            container.style.cssText = `
                position: fixed;
                top: 20px;
                right: 20px;
                z-index: 10001;
                display: flex;
                flex-direction: column;
                gap: 10px;
            `;
            document.body.appendChild(container);
        }
        return container;
    }

    show(message, type = 'info', duration = 3000) {
        const toast = document.createElement('div');
        toast.className = `toast ${type}`;
        
        const icons = {
            success: 'fa-check-circle',
            error: 'fa-times-circle',
            warning: 'fa-exclamation-triangle',
            info: 'fa-info-circle'
        };

        toast.innerHTML = `
            <i class="fas ${icons[type]}"></i>
            <span>${message}</span>
        `;

        this.container.appendChild(toast);

        setTimeout(() => {
            toast.style.animation = 'slideOut 0.3s ease';
            setTimeout(() => toast.remove(), 300);
        }, duration);
    }

    success(message) { this.show(message, 'success'); }
    error(message) { this.show(message, 'error'); }
    warning(message) { this.show(message, 'warning'); }
    info(message) { this.show(message, 'info'); }
}

// Add slideOut animation
const style = document.createElement('style');
style.textContent = `
    @keyframes slideOut {
        to {
            transform: translateX(400px);
            opacity: 0;
        }
    }
`;
document.head.appendChild(style);

// Initialize Toast
const toast = new ToastNotification();

// Confirm Dialog
function confirmDialog(message) {
    return new Promise((resolve) => {
        const dialog = document.createElement('div');
        dialog.className = 'modal active';
        dialog.innerHTML = `
            <div class="modal-content" style="max-width: 400px;">
                <div class="modal-header">
                    <h3>Xác nhận</h3>
                </div>
                <p style="margin-bottom: 20px;">${message}</p>
                <div style="display: flex; gap: 10px; justify-content: flex-end;">
                    <button class="btn btn-secondary" onclick="this.closest('.modal').remove(); window.confirmResult(false);">
                        Hủy
                    </button>
                    <button class="btn btn-danger" onclick="this.closest('.modal').remove(); window.confirmResult(true);">
                        Xác nhận
                    </button>
                </div>
            </div>
        `;
        document.body.appendChild(dialog);

        window.confirmResult = (result) => {
            delete window.confirmResult;
            resolve(result);
        };
    });
}

// Loading Overlay
function showLoading() {
    let overlay = document.getElementById('loading-overlay');
    if (!overlay) {
        overlay = document.createElement('div');
        overlay.id = 'loading-overlay';
        overlay.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background: rgba(0, 0, 0, 0.5);
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: 9999;
        `;
        overlay.innerHTML = '<div class="spinner"></div>';
        document.body.appendChild(overlay);
    }
    overlay.style.display = 'flex';
}

function hideLoading() {
    const overlay = document.getElementById('loading-overlay');
    if (overlay) {
        overlay.style.display = 'none';
    }
}

// Export to CSV
function exportTableToCSV(tableId, filename) {
    const table = document.getElementById(tableId);
    if (!table) {
        toast.error('Không tìm thấy bảng dữ liệu');
        return;
    }

    let csv = [];
    const rows = table.querySelectorAll('tr');

    for (let row of rows) {
        const cols = row.querySelectorAll('td, th');
        const csvRow = [];
        for (let col of cols) {
            csvRow.push('"' + col.innerText.replace(/"/g, '""') + '"');
        }
        csv.push(csvRow.join(','));
    }

    const csvContent = csv.join('\n');
    const blob = new Blob(['\ufeff' + csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = filename || 'export.csv';
    link.click();

    toast.success('Xuất file thành công!');
}

// Print Table
function printTable(containerId) {
    const content = document.getElementById(containerId);
    if (!content) {
        toast.error('Không tìm thấy nội dung để in');
        return;
    }

    const printWindow = window.open('', '', 'width=800,height=600');
    printWindow.document.write(`
        <html>
            <head>
                <title>Print</title>
                <style>
                    body { font-family: Arial, sans-serif; }
                    table { width: 100%; border-collapse: collapse; }
                    th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
                    th { background-color: #f2f2f2; }
                </style>
            </head>
            <body>
                ${content.innerHTML}
            </body>
        </html>
    `);
    printWindow.document.close();
    printWindow.print();
}

// Search Functionality
function setupSearch(inputId, tableId, columns) {
    const input = document.getElementById(inputId);
    const table = document.getElementById(tableId);
    
    if (!input || !table) return;

    input.addEventListener('input', function() {
        const filter = this.value.toLowerCase();
        const rows = table.getElementsByTagName('tr');

        for (let i = 1; i < rows.length; i++) {
            const row = rows[i];
            let found = false;

            for (let col of columns) {
                const cell = row.cells[col];
                if (cell && cell.textContent.toLowerCase().includes(filter)) {
                    found = true;
                    break;
                }
            }

            row.style.display = found ? '' : 'none';
        }
    });
}

// Date Range Filter
function createDateRangeFilter(containerId, onApply) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const filterHTML = `
        <div class="filter-group">
            <label>Từ ngày</label>
            <input type="date" id="startDate">
        </div>
        <div class="filter-group">
            <label>Đến ngày</label>
            <input type="date" id="endDate">
        </div>
        <button class="btn btn-primary" id="applyDateFilter">
            <i class="fas fa-filter"></i> Áp dụng
        </button>
    `;

    container.innerHTML = filterHTML;

    document.getElementById('applyDateFilter').addEventListener('click', () => {
        const startDate = document.getElementById('startDate').value;
        const endDate = document.getElementById('endDate').value;
        onApply(startDate, endDate);
    });
}

// Local Storage Helper
const LocalStorage = {
    set(key, value) {
        try {
            localStorage.setItem(key, JSON.stringify(value));
        } catch (e) {
            console.error('LocalStorage set error:', e);
        }
    },

    get(key, defaultValue = null) {
        try {
            const item = localStorage.getItem(key);
            return item ? JSON.parse(item) : defaultValue;
        } catch (e) {
            console.error('LocalStorage get error:', e);
            return defaultValue;
        }
    },

    remove(key) {
        localStorage.removeItem(key);
    },

    clear() {
        localStorage.clear();
    }
};

// Session Storage Helper
const SessionStorage = {
    set(key, value) {
        try {
            sessionStorage.setItem(key, JSON.stringify(value));
        } catch (e) {
            console.error('SessionStorage set error:', e);
        }
    },

    get(key, defaultValue = null) {
        try {
            const item = sessionStorage.getItem(key);
            return item ? JSON.parse(item) : defaultValue;
        } catch (e) {
            console.error('SessionStorage get error:', e);
            return defaultValue;
        }
    },

    remove(key) {
        sessionStorage.removeItem(key);
    },

    clear() {
        sessionStorage.clear();
    }
};

// Debounce function
function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

// Throttle function
function throttle(func, limit) {
    let inThrottle;
    return function(...args) {
        if (!inThrottle) {
            func.apply(this, args);
            inThrottle = true;
            setTimeout(() => inThrottle = false, limit);
        }
    };
}

// Format file size
function formatFileSize(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
}

// Copy to clipboard
function copyToClipboard(text) {
    if (navigator.clipboard) {
        navigator.clipboard.writeText(text).then(() => {
            toast.success('Đã sao chép!');
        }).catch(() => {
            toast.error('Sao chép thất bại!');
        });
    } else {
        // Fallback
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        document.body.removeChild(textarea);
        toast.success('Đã sao chép!');
    }
}

// Generate random ID
function generateId() {
    return '_' + Math.random().toString(36).substr(2, 9);
}

// Validate email
function isValidEmail(email) {
    const re = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return re.test(email);
}

// Validate phone
function isValidPhone(phone) {
    const re = /^[0-9]{10,11}$/;
    return re.test(phone.replace(/[\s\-]/g, ''));
}

// Create skeleton loading
function showSkeleton(containerId, rows = 5) {
    const container = document.getElementById(containerId);
    if (!container) return;

    let html = '';
    for (let i = 0; i < rows; i++) {
        html += `
            <tr>
                <td colspan="100%">
                    <div class="skeleton skeleton-text"></div>
                </td>
            </tr>
        `;
    }
    container.innerHTML = html;
}

// Retry API call
async function retryFetch(url, options = {}, retries = 3) {
    for (let i = 0; i < retries; i++) {
        try {
            const response = await fetch(url, options);
            if (response.ok) return response;
        } catch (error) {
            if (i === retries - 1) throw error;
            await new Promise(resolve => setTimeout(resolve, 1000 * (i + 1)));
        }
    }
}

// Download file
function downloadFile(url, filename) {
    fetch(url)
        .then(response => response.blob())
        .then(blob => {
            const link = document.createElement('a');
            link.href = URL.createObjectURL(blob);
            link.download = filename;
            link.click();
            toast.success('Tải xuống thành công!');
        })
        .catch(() => {
            toast.error('Tải xuống thất bại!');
        });
}

// Check if element is in viewport
function isInViewport(element) {
    const rect = element.getBoundingClientRect();
    return (
        rect.top >= 0 &&
        rect.left >= 0 &&
        rect.bottom <= (window.innerHeight || document.documentElement.clientHeight) &&
        rect.right <= (window.innerWidth || document.documentElement.clientWidth)
    );
}

// Smooth scroll to element
function smoothScrollTo(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
}

// Initialize tooltips (simple version)
function initTooltips() {
    document.querySelectorAll('[data-tooltip]').forEach(element => {
        element.addEventListener('mouseenter', function() {
            const tooltip = document.createElement('div');
            tooltip.className = 'tooltip';
            tooltip.textContent = this.getAttribute('data-tooltip');
            tooltip.style.cssText = `
                position: absolute;
                background: #333;
                color: white;
                padding: 5px 10px;
                border-radius: 5px;
                font-size: 12px;
                pointer-events: none;
                z-index: 10000;
            `;
            document.body.appendChild(tooltip);

            const rect = this.getBoundingClientRect();
            tooltip.style.top = (rect.top - tooltip.offsetHeight - 5) + 'px';
            tooltip.style.left = (rect.left + rect.width / 2 - tooltip.offsetWidth / 2) + 'px';

            this._tooltip = tooltip;
        });

        element.addEventListener('mouseleave', function() {
            if (this._tooltip) {
                this._tooltip.remove();
                delete this._tooltip;
            }
        });
    });
}

// Export utilities
window.AdminUtils = {
    toast,
    confirmDialog,
    showLoading,
    hideLoading,
    exportTableToCSV,
    printTable,
    setupSearch,
    createDateRangeFilter,
    LocalStorage,
    SessionStorage,
    debounce,
    throttle,
    formatFileSize,
    copyToClipboard,
    generateId,
    isValidEmail,
    isValidPhone,
    showSkeleton,
    retryFetch,
    downloadFile,
    isInViewport,
    smoothScrollTo,
    initTooltips
};

console.log('Admin Utils loaded successfully!');

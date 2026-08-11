(function () {
    'use strict';

    function ready(fn) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', fn, { once: true });
            return;
        }
        fn();
    }

    function resolveLoadingTarget(target) {
        if (!target) return document.body;
        if (typeof target === 'string') return document.querySelector(target) || document.body;
        return target;
    }

    function isButtonLike(element) {
        if (!element || !element.matches) return false;
        return element.matches('button, input[type="submit"], input[type="button"], .btn, [role="button"]');
    }

    function isFieldLike(element) {
        if (!element || !element.matches) return false;
        return element.matches('input:not([type="submit"]):not([type="button"]), textarea, select');
    }

    function createLoadingOverlay(text) {
        var overlay = document.createElement('div');
        overlay.className = 'wms-loading-overlay';
        overlay.setAttribute('role', 'status');
        overlay.setAttribute('aria-live', 'polite');
        overlay.innerHTML = '<span class="wms-loading-spinner" aria-hidden="true"></span><span class="wms-loading-text"></span>';
        overlay.querySelector('.wms-loading-text').textContent = text || '\u0110ang x\u1eed l\u00fd...';
        return overlay;
    }

    function beginLoading(target, options) {
        var settings = options || {};
        var element = resolveLoadingTarget(target);
        var delay = Number.isFinite(settings.delay) ? settings.delay : 700;
        var handle = {
            target: element,
            timer: null,
            visible: false,
            overlay: null,
            isButton: isButtonLike(element),
            isField: isFieldLike(element),
            originalHtml: element && element.innerHTML,
            originalDisabled: element && 'disabled' in element ? element.disabled : null,
            hadRegionClass: element && element.classList ? element.classList.contains('wms-loading-region') : false
        };

        if (!element) return handle;

        element.setAttribute('aria-busy', 'true');
        if (handle.isButton && settings.disable !== false && 'disabled' in element) {
            element.disabled = true;
            element.setAttribute('aria-disabled', 'true');
        }

        handle.timer = window.setTimeout(function () {
            handle.visible = true;
            element.classList.add('is-wms-loading');

            if (handle.isButton) {
                element.classList.add('enterprise-submit-loading');
                if (settings.text) {
                    element.textContent = '';
                    var label = document.createElement('span');
                    label.textContent = settings.text;
                    element.appendChild(label);
                }
                return;
            }

            if (handle.isField) {
                element.classList.add('wms-loading-field');
                return;
            }

            element.classList.add('wms-loading-region');
            handle.overlay = createLoadingOverlay(settings.text);
            element.appendChild(handle.overlay);
        }, Math.max(0, delay));

        return handle;
    }

    function endLoading(handle) {
        if (!handle || !handle.target) return;
        var element = handle.target;
        if (handle.timer) window.clearTimeout(handle.timer);

        element.removeAttribute('aria-busy');
        element.classList.remove('is-wms-loading');

        if (handle.isButton) {
            element.classList.remove('enterprise-submit-loading');
            if (handle.originalHtml != null) element.innerHTML = handle.originalHtml;
            if ('disabled' in element && handle.originalDisabled != null) {
                element.disabled = handle.originalDisabled;
            }
            element.removeAttribute('aria-disabled');
            return;
        }

        if (handle.isField) {
            element.classList.remove('wms-loading-field');
            return;
        }

        if (handle.overlay && handle.overlay.parentNode) {
            handle.overlay.parentNode.removeChild(handle.overlay);
        }
        if (!handle.hadRegionClass) {
            element.classList.remove('wms-loading-region');
        }
    }

    function withBusy(target, taskOrPromise, options) {
        var handle = beginLoading(target, options);
        try {
            var result = typeof taskOrPromise === 'function' ? taskOrPromise() : taskOrPromise;
            return Promise.resolve(result).finally(function () {
                endLoading(handle);
            });
        } catch (error) {
            endLoading(handle);
            throw error;
        }
    }

    window.wmsLoading = {
        begin: beginLoading,
        end: endLoading,
        withBusy: withBusy
    };

    function enhanceTables() {
        document.querySelectorAll('.main-content table').forEach(function (table) {
            if (table.closest('.print-page, .print-sheet, .label-page, .no-enterprise-enhance')) return;

            table.classList.add('enterprise-table');

            var parent = table.parentElement;
            var needsWrap = parent
                && !parent.classList.contains('table-responsive')
                && !parent.classList.contains('table-container')
                && !parent.classList.contains('yardops-table-wrap')
                && !parent.classList.contains('enterprise-table-wrap');

            if (needsWrap) {
                var wrapper = document.createElement('div');
                wrapper.className = 'enterprise-table-wrap';
                parent.insertBefore(wrapper, table);
                wrapper.appendChild(table);
            }

            var headers = Array.from(table.querySelectorAll('thead th')).map(function (th) {
                return th.textContent.trim();
            });
            if (!headers.length) return;

            table.querySelectorAll('tbody tr').forEach(function (row) {
                Array.from(row.children).forEach(function (cell, index) {
                    if (!cell.dataset.label && headers[index]) {
                        cell.dataset.label = headers[index];
                    }
                });
            });
        });
    }

    function ensureAccessibleIconAction(element) {
        if (!element || !element.matches) return;
        var text = (element.textContent || '').trim();
        if (text || element.getAttribute('aria-label')) return;

        var label = element.getAttribute('title')
            || element.dataset.actionLabel
            || element.dataset.userAction
            || 'Thao tác';
        element.setAttribute('aria-label', label);
        if (!element.getAttribute('title')) element.setAttribute('title', label);
    }

    function enhanceEnterpriseActionColumns() {
        document.querySelectorAll('.main-content table').forEach(function (table) {
            if (table.closest('.print-page, .print-sheet, .label-page, .no-enterprise-enhance')) return;

            var hasActionCells = table.querySelector('td.td-actions, td.enterprise-action-cell') != null;
            if (!hasActionCells) return;

            table.classList.add('enterprise-sticky-actions');
            var parent = table.parentElement;
            if (parent) parent.classList.add('enterprise-sticky-action-wrap');

            var rows = table.querySelectorAll('tr');
            rows.forEach(function (row) {
                var last = row.lastElementChild;
                if (last) last.classList.add('enterprise-action-cell');
            });
        });

        document.querySelectorAll('.main-content .td-actions button, .main-content .td-actions a.btn, .main-content .enterprise-action-cell button, .main-content .enterprise-action-cell a.btn')
            .forEach(ensureAccessibleIconAction);
    }

    function enhanceForms() {
        document.querySelectorAll('.main-content form').forEach(function (form) {
            if (form.closest('.no-enterprise-enhance')) return;
            form.classList.add('enterprise-enhanced-form');
        });

        document.addEventListener('submit', function (event) {
            if (event.defaultPrevented) return;
            var form = event.target;
            if (!(form instanceof HTMLFormElement)) return;
            if (form.dataset.noSubmitLoading === 'true') return;
            var submitter = event.submitter;
            if (!(submitter instanceof HTMLButtonElement)) return;
            if (submitter.dataset.noSubmitLoading === 'true') return;
            beginLoading(submitter, {
                delay: Number(submitter.dataset.loadingDelay || form.dataset.loadingDelay || 700),
                text: submitter.dataset.loadingText || form.dataset.loadingText || null
            });
        }, true);
    }

    function enhanceStatusBadges() {
        document.querySelectorAll('.status-badge, .badge').forEach(function (badge) {
            var text = badge.textContent.trim().toLocaleLowerCase('vi');
            if (!text) return;
            if (/(lỗi|hủy|chặn|quá hạn|thất bại|dead)/.test(text)) badge.classList.add('badge-danger');
            else if (/(hoàn tất|đã gửi|đã xác nhận|ổn định|hoạt động|success)/.test(text)) badge.classList.add('badge-success');
            else if (/(chờ|nháp|đang|cảnh báo|warning)/.test(text)) badge.classList.add('badge-warning');
            else if (/(mới|thông tin|info)/.test(text)) badge.classList.add('badge-info');
        });
    }

    function enhanceDataWidths() {
        document.querySelectorAll('[data-progress-width], [data-segment-width]').forEach(function (element) {
            var width = element.dataset.progressWidth || element.dataset.segmentWidth;
            if (!width) return;
            element.style.width = width;
        });
    }

    window.enhanceDataWidths = enhanceDataWidths;

    function parseActionValue(value) {
        if (value == null) return value;
        if (value === 'true') return true;
        if (value === 'false') return false;
        if (/^-?\d+(\.\d+)?$/.test(value)) return Number(value);
        return value;
    }

    function parseActionArgs(element) {
        if (element.dataset.wmsJsonArgs) {
            try {
                return JSON.parse(element.dataset.wmsJsonArgs);
            } catch (error) {
                return [];
            }
        }

        return ['wmsArg', 'wmsArg2', 'wmsArg3', 'wmsArg4']
            .filter(function (key) { return Object.prototype.hasOwnProperty.call(element.dataset, key); })
            .map(function (key) { return parseActionValue(element.dataset[key]); });
    }

    function callNamedAction(name, args, sourceElement) {
        if (!name || typeof window[name] !== 'function') return false;
        window[name].apply(window, args || []);
        if (sourceElement) sourceElement.dispatchEvent(new CustomEvent('wms:action-called', { bubbles: true, detail: { action: name } }));
        return true;
    }

    function closeModalById(id) {
        var modal = document.getElementById(id);
        if (!modal) return;
        if (modal.classList.contains('active')) modal.classList.remove('active');
        if (modal.classList.contains('is-open')) modal.classList.remove('is-open');
        if (modal.style.display && modal.style.display !== 'none') modal.style.display = 'none';
        modal.setAttribute('aria-hidden', 'true');
    }

    function openModalById(id) {
        var modal = document.getElementById(id);
        if (!modal) return;
        modal.classList.add('active');
        if (modal.style.display === 'none') modal.style.display = 'flex';
        modal.removeAttribute('aria-hidden');
    }

    function runDataAction(element, event) {
        if (!element || element.disabled || element.getAttribute('aria-disabled') === 'true') return;

        if (element.dataset.wmsWindowAction) {
            event.preventDefault();
            var action = element.dataset.wmsWindowAction;
            if (action === 'print') window.print();
            else if (action === 'close') window.close();
            else if (action === 'back') window.history.back();
            return;
        }

        if (element.dataset.wmsNotifyTitle) {
            event.preventDefault();
            if (typeof window.enterpriseNotify === 'function') {
                window.enterpriseNotify({
                    title: element.dataset.wmsNotifyTitle,
                    text: element.dataset.wmsNotifyText || '',
                    icon: element.dataset.wmsNotifyIcon || 'info'
                });
            }
            return;
        }

        if (element.dataset.wmsClickTarget) {
            event.preventDefault();
            var target = document.querySelector(element.dataset.wmsClickTarget);
            if (target) target.click();
            return;
        }

        if (element.dataset.wmsExportTable) {
            event.preventDefault();
            callNamedAction('exportTableToExcel', [element.dataset.wmsExportTable, element.dataset.wmsExportFilename || 'wms_export'], element);
            return;
        }

        if (element.dataset.wmsCloseModal) {
            event.preventDefault();
            closeModalById(element.dataset.wmsCloseModal);
            return;
        }

        if (element.dataset.wmsOpenModal) {
            event.preventDefault();
            openModalById(element.dataset.wmsOpenModal);
            return;
        }

        if (element.dataset.wmsCallSelf) {
            event.preventDefault();
            callNamedAction(element.dataset.wmsCallSelf, [element], element);
            return;
        }

        if (element.dataset.wmsCalls) {
            event.preventDefault();
            try {
                JSON.parse(element.dataset.wmsCalls).forEach(function (call) {
                    if (!Array.isArray(call) || call.length === 0) return;
                    callNamedAction(call[0], call.slice(1), element);
                });
            } catch (error) {
                return;
            }
            return;
        }

        if (element.dataset.wmsCall) {
            event.preventDefault();
            callNamedAction(element.dataset.wmsCall, parseActionArgs(element), element);
        }
    }

    function enhanceDataActions() {
        document.addEventListener('click', function (event) {
            var element = event.target.closest('[data-wms-window-action], [data-wms-notify-title], [data-wms-click-target], [data-wms-export-table], [data-wms-close-modal], [data-wms-open-modal], [data-wms-call-self], [data-wms-calls], [data-wms-call]');
            if (!element) return;
            runDataAction(element, event);
        });

        document.addEventListener('change', function (event) {
            var element = event.target;
            if (!(element instanceof HTMLElement)) return;
            if (element.dataset.wmsSubmitForm === 'true' && element.form) element.form.submit();
            if (element.dataset.wmsRedirectRoute) {
                var param = element.dataset.wmsRedirectParam || element.name || 'value';
                window.location.href = element.dataset.wmsRedirectRoute + '?' + encodeURIComponent(param) + '=' + encodeURIComponent(element.value || '');
            }
            if (element.dataset.wmsClearValidity === 'true' && typeof element.setCustomValidity === 'function') element.setCustomValidity('');
            if (element.dataset.wmsChangeCall) {
                var changeArgs = element.dataset.wmsChangeSelf === 'true'
                    ? [element]
                    : element.dataset.wmsChangeValue === 'true'
                        ? [parseActionValue(element.value)]
                        : parseActionArgs(element);
                callNamedAction(element.dataset.wmsChangeCall, changeArgs, element);
            }
        });

        document.addEventListener('input', function (event) {
            var element = event.target;
            if (!(element instanceof HTMLElement)) return;
            if (element.dataset.wmsClearValidity === 'true' && typeof element.setCustomValidity === 'function') element.setCustomValidity('');
            if (element.dataset.wmsInputCall) callNamedAction(element.dataset.wmsInputCall, element.dataset.wmsInputSelf === 'true' ? [element] : parseActionArgs(element), element);
        });

        document.addEventListener('invalid', function (event) {
            var element = event.target;
            if (!(element instanceof HTMLElement)) return;
            if (element.dataset.wmsInvalidMessage && typeof element.setCustomValidity === 'function') {
                element.setCustomValidity(element.dataset.wmsInvalidMessage);
            }
        }, true);
    }

    function parseJsonDataset(element, key) {
        if (!element || !element.dataset[key]) return [];
        try {
            var value = JSON.parse(element.dataset[key]);
            return Array.isArray(value) ? value : [];
        } catch (error) {
            return [];
        }
    }

    function hasPositiveSeriesData(series) {
        return series.some(function (items) {
            return Array.isArray(items) && items.some(function (value) {
                return Number(value || 0) > 0;
            });
        });
    }

    function renderEmptyChartState(canvas, title, note) {
        if (!canvas || canvas.dataset.emptyChartRendered === 'true') return;
        canvas.dataset.emptyChartRendered = 'true';
        canvas.hidden = true;

        var state = document.createElement('div');
        state.className = 'enterprise-empty-chart';
        state.setAttribute('role', 'status');
        state.innerHTML = '<span class="enterprise-empty-chart-icon" aria-hidden="true"><i class="fas fa-chart-line"></i></span><strong></strong><small></small>';
        state.querySelector('strong').textContent = title;
        state.querySelector('small').textContent = note;
        canvas.insertAdjacentElement('afterend', state);

        var card = canvas.closest('.analytics-chart-card');
        if (card) card.classList.add('is-empty');
    }

    function initReportAnalyticsCharts() {
        var data = document.querySelector('[data-wms-analytics-data="true"]');
        if (!data) return;

        var labels = parseJsonDataset(data, 'chartLabels');
        var inbound = parseJsonDataset(data, 'inbound');
        var outbound = parseJsonDataset(data, 'outbound');
        var lines = parseJsonDataset(data, 'lines');
        var isDark = document.documentElement.getAttribute('data-theme') === 'dark';
        var gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.06)';
        var txtColor = isDark ? '#a0a8b8' : '#64748b';
        var commonScales = {
            x: { ticks: { color: txtColor }, grid: { color: gridColor } },
            y: { beginAtZero: true, ticks: { color: txtColor }, grid: { color: gridColor } }
        };

        var throughput = document.querySelector('[data-wms-analytics-chart="throughput"]');
        if (throughput) {
            if (!hasPositiveSeriesData([inbound, outbound])) {
                renderEmptyChartState(throughput, 'Chưa có lưu lượng nhập xuất trong kỳ lọc', 'Mở rộng khoảng thời gian, chọn kho khác hoặc phát sinh phiếu để hiển thị đường xu hướng.');
            } else if (typeof window.Chart === 'function') {
            new window.Chart(throughput, {
                type: 'line',
                data: {
                    labels: labels,
                    datasets: [
                        {
                            label: 'Nhập kho',
                            data: inbound,
                            borderColor: '#10b981',
                            backgroundColor: 'rgba(16,185,129,0.10)',
                            fill: true,
                            tension: 0.3,
                            pointRadius: 2
                        },
                        {
                            label: 'Xuất kho',
                            data: outbound,
                            borderColor: '#f59e0b',
                            backgroundColor: 'rgba(245,158,11,0.10)',
                            fill: true,
                            tension: 0.3,
                            pointRadius: 2
                        }
                    ]
                },
                options: {
                    responsive: true,
                    plugins: { legend: { labels: { color: txtColor } } },
                    scales: commonScales
                }
            });
            } else {
                renderEmptyChartState(throughput, 'Biểu đồ chưa sẵn sàng', 'Dữ liệu đã có, nhưng thư viện biểu đồ chưa tải xong. Bảng số liệu vẫn là nguồn đối chiếu chính.');
            }
        }

        var lineChart = document.querySelector('[data-wms-analytics-chart="lines"]');
        if (lineChart) {
            if (!hasPositiveSeriesData([lines])) {
                renderEmptyChartState(lineChart, 'Chưa có dòng hàng xử lý theo ngày', 'Khi phiếu phát sinh dòng hàng, biểu đồ sẽ hiển thị khối lượng theo từng ngày.');
            } else if (typeof window.Chart === 'function') {
            new window.Chart(lineChart, {
                type: 'bar',
                data: {
                    labels: labels,
                    datasets: [{
                        label: 'Dòng hàng',
                        data: lines,
                        backgroundColor: 'rgba(99,102,241,0.60)',
                        borderColor: '#6366f1',
                        borderWidth: 1,
                        borderRadius: 4
                    }]
                },
                options: {
                    responsive: true,
                    plugins: { legend: { labels: { color: txtColor } } },
                    scales: commonScales
                }
            });
            } else {
                renderEmptyChartState(lineChart, 'Biểu đồ chưa sẵn sàng', 'Dữ liệu đã có, nhưng thư viện biểu đồ chưa tải xong. Bảng số liệu vẫn là nguồn đối chiếu chính.');
            }
        }
    }

    window.applyFilter = function () {
        var warehouse = document.getElementById('filterWarehouse');
        var url = new URL(window.location.pathname, window.location.origin);
        if (warehouse && warehouse.value) url.searchParams.set('warehouseId', warehouse.value);
        window.location.href = url.toString();
    };

    window.openScheduledReportModal = function () {
        document.getElementById('rptModal')?.classList.add('is-open');
    };

    window.closeScheduledReportModal = function () {
        document.getElementById('rptModal')?.classList.remove('is-open');
    };

    window.toggleScheduleFields = function () {
        var schedule = document.getElementById('scheduleType');
        var value = schedule ? schedule.value : '';
        document.getElementById('weeklyField')?.classList.toggle('d-none', value !== 'Weekly');
        document.getElementById('monthlyField')?.classList.toggle('d-none', value !== 'Monthly');
    };

    ready(function () {
        document.body.classList.add('enterprise-ui-ready');
        enhanceTables();
        enhanceEnterpriseActionColumns();
        enhanceForms();
        enhanceStatusBadges();
        enhanceDataWidths();
        enhanceDataActions();
        initReportAnalyticsCharts();
    });
})();

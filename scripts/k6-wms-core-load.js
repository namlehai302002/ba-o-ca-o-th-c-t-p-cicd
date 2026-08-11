import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend, Rate } from 'k6/metrics';

const baseUrl = __ENV.WMS_BASE_URL;
const authCookie = __ENV.WMS_AUTH_COOKIE || '';
const csrfToken = __ENV.WMS_CSRF_TOKEN || '';
const enableWrites = (__ENV.WMS_K6_ENABLE_WRITES || '').toLowerCase() === 'true';
const runPrefix = __ENV.WMS_K6_PREFIX || `K6-${Date.now()}`;

if (!baseUrl) {
  throw new Error('WMS_BASE_URL is required. Use a staging/local URL, never production without approval.');
}

export const options = {
  scenarios: {
    smoke_10: { executor: 'constant-vus', vus: 10, duration: '1m', tags: { tier: '10-user' } },
    steady_50: { executor: 'constant-vus', vus: 50, duration: '2m', startTime: '1m10s', tags: { tier: '50-user' } },
    stress_100: { executor: 'constant-vus', vus: 100, duration: '2m', startTime: '3m20s', tags: { tier: '100-user' } },
    peak_200: { executor: 'constant-vus', vus: 200, duration: '1m', startTime: '5m30s', tags: { tier: '200-user' } }
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1500', 'p(99)<3000'],
    wms_business_error_rate: ['rate<0.01'],
    wms_report_latency: ['p(95)<2000']
  }
};

const businessErrorRate = new Rate('wms_business_error_rate');
const reportLatency = new Trend('wms_report_latency');

function headers(extra = {}) {
  return {
    Cookie: authCookie,
    'RequestVerificationToken': csrfToken,
    'X-WMS-Load-Test': runPrefix,
    ...extra
  };
}

function get(path, name) {
  const res = http.get(`${baseUrl}${path}`, { headers: headers(), tags: { name } });
  const ok = check(res, {
    [`${name} status < 500`]: r => r.status < 500,
    [`${name} not login page when authenticated`]: r => authCookie ? !String(r.body).includes('/Account/Login') : true
  });
  businessErrorRate.add(!ok);
  return res;
}

export default function () {
  group('dashboard and reports', () => {
    get('/', 'dashboard');
    const inventory = get('/Reports/Inventory', 'inventory-report');
    reportLatency.add(inventory.timings.duration);
    const movement = get('/Reports/StockMovement', 'stock-movement-report');
    reportLatency.add(movement.timings.duration);
    const transactions = get('/Reports/InventoryTransactions', 'inventory-transactions-report');
    reportLatency.add(transactions.timings.duration);
  });

  group('inbound outbound reservation picking pages', () => {
    get('/Vouchers/Create?type=NhapKho', 'inbound-create');
    get('/Vouchers/Create?type=XuatKho', 'outbound-create');
    get('/Operations/PickTasks', 'pick-tasks');
    get('/Operations/RfPicking', 'rf-picking');
  });

  if (enableWrites) {
    group('write smoke requires explicit opt-in', () => {
      const body = {
        ReferenceNo: `${runPrefix}-WRITE-SMOKE`,
        Description: 'k6 opt-in write smoke; cleanup required by operator'
      };
      const res = http.post(`${baseUrl}/Vouchers/Create?type=NhapKho`, body, {
        headers: headers({ 'Content-Type': 'application/x-www-form-urlencoded' }),
        tags: { name: 'inbound-write-smoke' }
      });
      const ok = check(res, {
        'write smoke did not return 5xx': r => r.status < 500
      });
      businessErrorRate.add(!ok);
    });
  }

  sleep(1);
}


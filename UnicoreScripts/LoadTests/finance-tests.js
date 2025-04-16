import http from 'k6/http';
import { sleep, check } from 'k6';
import { Counter } from 'k6/metrics';

// Custom metrics
const successfulPayments = new Counter('successful_payments');
const failedPayments = new Counter('failed_payments');

export const options = {
  scenarios: {
    constant_request_rate: {
      executor: 'constant-arrival-rate',
      rate: 1000, // 1000 RPS
      timeUnit: '1s',
      duration: '20s',
      preAllocatedVUs: 100, // Initial pool of VUs
      maxVUs: 500, // Maximum number of VUs to handle the load
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'], // Error rate should be less than 5%
    http_req_duration: ['p(95)<1000'], // 95% of requests should be below 1s
  },
};

// Generate a random claim ID
function generateClaimId() {
  return `CLM-${Math.floor(Math.random() * 1000000)}`;
}

// Generate a random policy number
function generatePolicyNumber() {
  const policies = ['POL-12345', 'POL-67890', 'POL-54321'];
  return policies[Math.floor(Math.random() * policies.length)];
}

// Generate a random amount between 100 and 10000
function generateAmount() {
  return Math.floor(Math.random() * 9900) + 100;
}

// Generate a random payee ID
function generatePayeeId() {
  return `USER-${Math.floor(Math.random() * 10000)}`;
}

export default function () {
  const baseUrl = 'http://localhost:1301'; // Updated Finance Service port
  
  // Health check endpoint test
  {
    const healthRes = http.get(`${baseUrl}/api/finance/health`);
    check(healthRes, {
      'health check status is 200': (r) => r.status === 200,
      'health check response has correct service': (r) => r.json('Service') === 'Unicore Finance Service',
    });
  }
  
  // Process payment endpoint test
  {
    const payload = JSON.stringify({
      claimId: generateClaimId(),
      policyNumber: generatePolicyNumber(),
      amount: generateAmount(),
      payeeId: generatePayeeId()
    });
    
    const params = {
      headers: {
        'Content-Type': 'application/json',
      },
    };
    
    const processRes = http.post(`${baseUrl}/api/finance/process-payment`, payload, params);
    
    const isSuccessful = check(processRes, {
      'process payment status is 200': (r) => r.status === 200,
      'process payment response has paymentId': (r) => r.json('PaymentId') !== undefined,
      'process payment response has success': (r) => r.json('Success') === true,
    });
    
    if (isSuccessful) {
      successfulPayments.add(1);
    } else {
      failedPayments.add(1);
      console.log(`Failed payment request: ${processRes.status}, ${processRes.body}`);
    }
  }
  
  // Add a small delay between iterations to simulate more realistic user behavior
  sleep(0.1);
}

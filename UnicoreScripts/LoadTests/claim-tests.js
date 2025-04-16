import http from 'k6/http';
import { sleep, check } from 'k6';
import { Counter } from 'k6/metrics';

// Custom metrics
const successfulClaims = new Counter('successful_claims');
const failedClaims = new Counter('failed_claims');

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

// Generate a random policy number (from our known valid policies)
function generatePolicyNumber() {
  const policies = ['POL-12345', 'POL-67890', 'POL-54321'];
  return policies[Math.floor(Math.random() * policies.length)];
}

// Generate a random amount between 100 and 10000
function generateAmount() {
  return Math.floor(Math.random() * 9900) + 100;
}

export default function () {
  const baseUrl = 'http://localhost:1300'; // Updated Claim Service port
  
  // Health check endpoint test
  {
    const healthRes = http.get(`${baseUrl}/api/claim/health`);
    check(healthRes, {
      'health check status is 200': (r) => r.status === 200,
      'health check response has correct service': (r) => r.json('Service') === 'Unicore Claim Service',
    });
  }
  
  // Process claim endpoint test
  {
    const payload = JSON.stringify({
      claimId: generateClaimId(),
      policyNumber: generatePolicyNumber(),
      amount: generateAmount(),
      description: 'Load test claim',
      userId: 'TestUser',
      processingType: 'Standard',
      priority: 'Medium',
      category: 'Medical'
    });
    
    const params = {
      headers: {
        'Content-Type': 'application/json',
      },
    };
    
    const processRes = http.post(`${baseUrl}/api/claim/process`, payload, params);
    
    const isSuccessful = check(processRes, {
      'process claim status is 200': (r) => r.status === 200,
      'process claim response has status': (r) => r.json('Status') !== undefined,
    });
    
    if (isSuccessful) {
      successfulClaims.add(1);
    } else {
      failedClaims.add(1);
      console.log(`Failed claim request: ${processRes.status}, ${processRes.body}`);
    }
  }
  
  // Add a small delay between iterations to simulate more realistic user behavior
  sleep(0.1);
}

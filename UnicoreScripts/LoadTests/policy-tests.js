import http from 'k6/http';
import { sleep, check } from 'k6';
import { Counter } from 'k6/metrics';

// Custom metrics
const validPolicies = new Counter('valid_policies');
const invalidPolicies = new Counter('invalid_policies');

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

// Generate a policy number (mix of valid and invalid)
function generatePolicyNumber() {
  const validPolicies = ['POL-12345', 'POL-67890', 'POL-54321'];
  const invalidPolicies = ['POL-99999', 'POL-00000', 'POL-ABCDE'];
  
  // 70% chance of using a valid policy
  if (Math.random() < 0.7) {
    return validPolicies[Math.floor(Math.random() * validPolicies.length)];
  } else {
    return invalidPolicies[Math.floor(Math.random() * invalidPolicies.length)];
  }
}

export default function () {
  const baseUrl = 'http://localhost:1302'; // Updated Policy Service port
  
  // Health check endpoint test
  {
    const healthRes = http.get(`${baseUrl}/api/policy/health`);
    check(healthRes, {
      'health check status is 200': (r) => r.status === 200,
      'health check response has correct service': (r) => r.json('Service') === 'Policy Service',
    });
  }
  
  // Validate policy endpoint test
  {
    const policyNumber = generatePolicyNumber();
    const validateRes = http.get(`${baseUrl}/api/policy/validate/${policyNumber}`);
    
    // Check if the response is successful (even if policy is invalid, the API should return 200)
    const isSuccessful = check(validateRes, {
      'validate policy status is 200': (r) => r.status === 200,
      'validate policy response has isValid': (r) => r.json('IsValid') !== undefined,
    });
    
    if (isSuccessful) {
      // Track whether the policy was valid or invalid
      if (validateRes.json('IsValid')) {
        validPolicies.add(1);
      } else {
        invalidPolicies.add(1);
      }
    } else {
      console.log(`Failed policy validation: ${validateRes.status}, ${validateRes.body}`);
    }
  }
  
  // Add a small delay between iterations to simulate more realistic user behavior
  sleep(0.1);
}

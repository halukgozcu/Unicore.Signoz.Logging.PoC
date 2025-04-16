# Unicore SigNoz OpenTelemetry Demo

This project demonstrates the implementation of OpenTelemetry with Serilog for distributed tracing, metrics, and logging across multiple microservices.

## Services

The demo consists of three microservices:
- **Claim Service** (Port 1200): Handles claim processing and communicates with the Finance Service
- **Finance Service** (Port 1201): Processes payments and communicates with Policy Service
- **Policy Service** (Port 1202): Validates insurance policies

## Architecture

- **Distributed Tracing**: Using OpenTelemetry to trace requests across services
- **Metrics Collection**: Custom and system metrics tracked with OpenTelemetry
- **Logging**: Serilog integrated with OpenTelemetry for contextualized logs
- **SigNoz**: Telemetry backend that collects and visualizes traces, metrics, and logs
- **Load Testing**: k6 scripts to simulate high load on the services

## Prerequisites

- .NET 9 SDK
- Docker and Docker Compose v2+
- At least 4GB of RAM allocated to Docker
- k6 for load testing

## Setup

### 1. Install & Start SigNoz using Docker

1. Clone the SigNoz repository:
   ```bash
   git clone -b main https://github.com/SigNoz/signoz.git
   ```

2. Navigate to the SigNoz directory:
   ```bash
   cd signoz/deploy/docker/
   ```

3. Run the SigNoz installation script:
   ```bash
   ./install.sh
   ```

4. Once the installation is complete, SigNoz UI will be available at [http://localhost:3301](http://localhost:3301)

   **Note:** The default credentials are:
   - Email: admin@signoz.io
   - Password: admin

5. The OTLP endpoints will be:
   - OTLP HTTP: http://localhost:4318
   - OTLP gRPC: http://localhost:4317

### 2. Run the Services

You can run the services either manually or using the provided scripts.

#### Using Scripts (Recommended)

The `UnicoreScripts` directory contains scripts to manage all services:

```bash
# Start all services
cd UnicoreScripts
./watch-services.sh

# Stop all services
./stop-services.sh
```

#### Manual Start

Alternatively, you can start each service manually in separate terminal windows:

```bash
# Terminal 1
cd Unicore.Claim.Service
dotnet run

# Terminal 2
cd Unicore.Finance.Service
dotnet run

# Terminal 3 
cd Unicore.Policy.Service
dotnet run
```

## Testing the Services

Use the following curl command to simulate a claim process, which will initiate the distributed trace:

```bash
curl -X POST http://localhost:1200/api/claim/process \
-H "Content-Type: application/json" \
-d '{
    "claimId": "CLM-12345",
    "policyNumber": "POL-12345",
    "amount": 1000,
    "description": "Medical expense claim"
}'
```

## Load Testing with k6

The project includes k6 load test scripts to simulate high traffic for all services, helping you to analyze system behavior under load and stress test the observability implementation.

### 1. Installing k6

#### On macOS:
```bash
brew install k6
```

#### On Windows:
```powershell
# Using Chocolatey
choco install k6

# Or download the installer from https://dl.k6.io/msi/k6-latest-amd64.msi
```

#### On Linux:
```bash
# Using apt (Debian/Ubuntu)
sudo gpg -k
sudo gpg --no-default-keyring --keyring /usr/share/keyrings/k6-archive-keyring.gpg --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
echo "deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
sudo apt-get update
sudo apt-get install k6

# Using yum (CentOS/RHEL)
sudo dnf install https://dl.k6.io/rpm/repo.rpm
sudo dnf install k6
```

#### Using Docker:
```bash
docker pull grafana/k6
```

### 2. Running Load Tests

The load test scripts are located in the `UnicoreScripts/LoadTests` directory. You can run them individually or use the provided script to run all tests sequentially.

#### Running Individual Test Scripts:

```bash
# From the UnicoreScripts directory
k6 run LoadTests/claim-tests.js
k6 run LoadTests/finance-tests.js
k6 run LoadTests/policy-tests.js
```

#### Using Docker for k6:

```bash
# From the project root directory
docker run -i grafana/k6 run - <UnicoreScripts/LoadTests/claim-tests.js
docker run -i grafana/k6 run - <UnicoreScripts/LoadTests/finance-tests.js
docker run -i grafana/k6 run - <UnicoreScripts/LoadTests/policy-tests.js
```

#### Running All Tests Using Script:

```bash
# Make the script executable
chmod +x UnicoreScripts/run-load-tests.sh

# Run the script
./UnicoreScripts/run-load-tests.sh
```

This script will:
1. Start all three services in release mode
2. Run each k6 load test script with 1000 requests per second for 20 seconds
3. Stop all services when finished

### 3. Load Test Configuration

Each load test script is configured to:
- Generate 1000 requests per second
- Run for 20 seconds
- Use up to 500 virtual users to maintain the request rate
- Track success/failure metrics
- Add small delays between requests to simulate realistic user behavior

You can modify these settings in the scripts by adjusting the `options` object:

```javascript
export const options = {
  scenarios: {
    constant_request_rate: {
      executor: 'constant-arrival-rate',
      rate: 1000,           // requests per timeUnit
      timeUnit: '1s',       // 1000 RPS
      duration: '20s',      // test duration
      preAllocatedVUs: 100, // initial pool
      maxVUs: 500,          // maximum VUs
    },
  },
};
```

### 4. Analyzing Load Test Results

After running the tests, k6 will display summary statistics showing:
- HTTP request success/failure rates
- Response time metrics (min, max, median, p90, p95)
- Custom metrics specific to each service

These results help identify performance bottlenecks and verify that your distributed tracing and metrics collection work correctly under load.

## Exploring Telemetry in SigNoz

1. Open SigNoz UI at [http://localhost:3301](http://localhost:3301)
2. Navigate to the following sections:
   - **Traces**: View the full distributed traces between services
   - **Metrics**: Check custom metrics like request counters and application metrics
   - **Logs**: Examine the structured logs from all services with trace correlation
   - **Dashboards**: Create custom dashboards to monitor your application
   - **Alerts & Incidents**: Configure alerts based on metrics and trace data

## Features Implemented

- **Distributed Tracing**: End-to-end tracing across all three services
- **Context Propagation**: Automatic propagation of trace context between services
- **Custom Spans**: Manual instrumentation with custom spans and attributes
- **Metrics**: Both automatic and custom metrics collection
- **Logging with Serilog**: Enhanced logging with trace context correlation
- **Error Handling**: Proper error handling with trace status reporting
- **Service Identification**: Each service includes its name in telemetry data
- **Load Testing**: k6 scripts to test system behavior under high load

## Accessing Swagger UI

Each service has its own Swagger UI available at:

- Claim Service: http://localhost:1200/swagger
- Finance Service: http://localhost:1201/swagger  
- Policy Service: http://localhost:1202/swagger

## Troubleshooting

### SigNoz Issues

- If SigNoz UI is not accessible, check Docker logs:
  ```bash
  docker-compose -f signoz/deploy/docker/docker-compose.yaml logs -f
  ```

- Ensure you have allocated enough memory to Docker (at least 4GB)

- If you encounter database connectivity issues:
  ```bash
  docker-compose -f signoz/deploy/docker/docker-compose.yaml down -v
  docker-compose -f signoz/deploy/docker/docker-compose.yaml up -d
  ```

### Service Issues

- Make sure all NuGet packages are properly restored:
  ```bash
  dotnet restore
  ```

- If you're experiencing issues with Swagger UI, check that the route prefix is correctly set to "swagger" in the Program.cs files

- Verify that the services are listening on the correct ports (1200, 1201, 1202)

### Load Testing Issues

- If k6 tests fail to connect to services, make sure all services are running correctly
- For "connection refused" errors, check that the port numbers in the load test scripts match your service configurations
- If tests show higher than expected error rates, consider adjusting the VU count or request rate to match your system capability

## Stopping the Services

You can stop the services using either:
```bash
# Using the stop script (Recommended)
cd UnicoreScripts
./stop-services.sh

# OR stop SigNoz only
cd signoz/deploy/docker/
docker-compose down
```

To completely remove all SigNoz data:
```bash
cd signoz/deploy/docker/
docker-compose down -v
```

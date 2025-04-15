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

## Prerequisites

- .NET 9 SDK
- Docker and Docker Compose v2+
- At least 4GB of RAM allocated to Docker

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

Start each service in separate terminal windows:

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

## Stopping the Services

To stop SigNoz:
```bash
cd signoz/deploy/docker/
docker-compose down
```

To completely remove all SigNoz data:
```bash
cd signoz/deploy/docker/
docker-compose down -v
```

#!/bin/bash

# Define colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Define service paths
CLAIM_SERVICE_PATH="../Unicore.Claim.Service"
FINANCE_SERVICE_PATH="../Unicore.Finance.Service"
POLICY_SERVICE_PATH="../Unicore.Policy.Service"

# Define log files
LOG_DIR="logs"
CLAIM_LOG="$LOG_DIR/claim-service.log"
FINANCE_LOG="$LOG_DIR/finance-service.log"
POLICY_LOG="$LOG_DIR/policy-service.log"

# Create log directory
mkdir -p "$LOG_DIR"

# Function to check if a process is running
is_process_running() {
    local pid=$1
    if ps -p "$pid" > /dev/null; then
        return 0 # Process is running
    else
        return 1 # Process is not running
    fi
}

# Function to start a service
start_service() {
    local service_name=$1
    local service_path=$2
    local log_file=$3
    local port=$4
    
    echo -e "${YELLOW}Starting $service_name on port $port...${NC}"
    
    # Navigate to service directory and start in release mode
    pushd "$service_path" > /dev/null
    dotnet run --configuration Release > "$log_file" 2>&1 &
    local pid=$!
    popd > /dev/null
    
    # Wait for service to start
    echo -n "Waiting for $service_name to start..."
    local max_wait=30
    local count=0
    while ! curl -s "http://localhost:$port/api/health" > /dev/null && [ $count -lt $max_wait ]; do
        echo -n "."
        sleep 1
        ((count++))
        
        # Check if process is still running
        if ! is_process_running $pid; then
            echo -e "\n${RED}Error: $service_name failed to start. Check $log_file for details.${NC}"
            return 1
        fi
    done
    
    if [ $count -eq $max_wait ]; then
        echo -e "\n${RED}Error: $service_name did not start within $max_wait seconds.${NC}"
        return 1
    fi
    
    echo -e "\n${GREEN}$service_name is running with PID $pid${NC}"
    
    # Save PID for later cleanup
    echo $pid > "$LOG_DIR/$service_name.pid"
    return 0
}

# Function to run load test on a service
run_load_test() {
    local service_name=$1
    local test_script=$2
    
    echo -e "${YELLOW}Running $service_name load test...${NC}"
    
    # Check if k6 is installed
    if ! command -v k6 &> /dev/null; then
        echo -e "${RED}Error: k6 is not installed. Please install it from https://k6.io/docs/getting-started/installation/${NC}"
        return 1
    fi
    
    # Run k6 load test
    k6 run "LoadTests/$test_script"
    
    echo -e "${GREEN}$service_name load test completed${NC}"
    return 0
}

# Function to stop all services
stop_services() {
    echo -e "${YELLOW}Stopping all services...${NC}"
    
    for service in "claim" "finance" "policy"; do
        if [ -f "$LOG_DIR/$service.pid" ]; then
            local pid=$(cat "$LOG_DIR/$service.pid")
            if is_process_running "$pid"; then
                echo "Stopping $service service (PID: $pid)..."
                kill "$pid"
            fi
            rm "$LOG_DIR/$service.pid"
        fi
    done
    
    echo -e "${GREEN}All services stopped${NC}"
}

# Set up trap to ensure services are stopped on script exit
trap stop_services EXIT

# Start all services
start_service "claim" "$CLAIM_SERVICE_PATH" "$CLAIM_LOG" 1300 || exit 1
start_service "finance" "$FINANCE_SERVICE_PATH" "$FINANCE_LOG" 1301 || exit 1
start_service "policy" "$POLICY_SERVICE_PATH" "$POLICY_LOG" 1302 || exit 1

echo -e "${GREEN}All services started successfully${NC}"
echo -e "${YELLOW}Waiting 5 seconds before starting load tests...${NC}"
sleep 5

# Run load tests
run_load_test "Claim Service" "claim-tests.js" || exit 1
echo -e "${YELLOW}Waiting 5 seconds before next test...${NC}"
sleep 5

run_load_test "Finance Service" "finance-tests.js" || exit 1
echo -e "${YELLOW}Waiting 5 seconds before next test...${NC}"
sleep 5

run_load_test "Policy Service" "policy-tests.js" || exit 1

echo -e "${GREEN}All load tests completed successfully${NC}"

# The trap will handle stopping the services
exit 0

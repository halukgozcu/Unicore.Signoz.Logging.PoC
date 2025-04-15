#!/bin/bash

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

# Function to clean up processes
cleanup() {
    echo -e "\n${RED}Stopping all services...${NC}"
    kill $(jobs -p) 2>/dev/null
    exit 0
}

# Handle Ctrl+C and other termination signals
trap cleanup SIGINT SIGTERM

# Build common project first
echo -e "${GREEN}Building common project...${NC}"
dotnet build ../Unicore.Common.OpenTelemetry/Unicore.Common.OpenTelemetry.csproj || {
    echo -e "${RED}Failed to build common project${NC}"
    exit 1
}

# Start all services in watch mode
echo -e "${GREEN}Starting services...${NC}"
start_services() {
    # Start Claim Service
    echo -e "${GREEN}Starting Claim Service...${NC}"
    dotnet watch run --project ../Unicore.Claim.Service/Unicore.Claim.Service.csproj --no-build & 
    claim_pid=$!

    # Wait a bit to avoid dependency conflicts
    sleep 5

    # Start Policy Service
    echo -e "${GREEN}Starting Policy Service...${NC}"
    dotnet watch run --project ../Unicore.Policy.Service/Unicore.Policy.Service.csproj --no-build &
    policy_pid=$!

    # Wait a bit to avoid dependency conflicts
    sleep 5

    # Start Finance Service
    echo -e "${GREEN}Starting Finance Service...${NC}"
    dotnet watch run --project ../Unicore.Finance.Service/Unicore.Finance.Service.csproj --no-build &
    finance_pid=$!

    # Wait for all processes
    wait $claim_pid $policy_pid $finance_pid
}

# Run the services
start_services

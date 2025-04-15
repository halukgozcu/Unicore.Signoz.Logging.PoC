#!/bin/bash

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

echo -e "${RED}Stopping all Unicore services...${NC}"

# Find and kill processes by service name
services=("Unicore.Claim.Service" "Unicore.Policy.Service" "Unicore.Finance.Service")

for service in "${services[@]}"; do
    pids=$(pgrep -f "$service")
    if [ ! -z "$pids" ]; then
        echo -e "${GREEN}Stopping $service (PIDs: $pids)${NC}"
        kill $pids 2>/dev/null
    else
        echo -e "${RED}No running process found for $service${NC}"
    fi
done

# Double-check for any remaining dotnet watch processes
watch_pids=$(pgrep -f "dotnet watch")
if [ ! -z "$watch_pids" ]; then
    echo -e "${GREEN}Cleaning up remaining watch processes (PIDs: $watch_pids)${NC}"
    kill $watch_pids 2>/dev/null
fi

echo -e "${GREEN}All services stopped${NC}"

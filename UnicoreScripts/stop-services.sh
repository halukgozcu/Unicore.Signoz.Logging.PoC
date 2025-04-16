#!/bin/bash

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
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

# Kill processes running on specific ports
echo -e "${YELLOW}Checking for processes on service ports...${NC}"
ports=(1300 1301 1302)

for port in "${ports[@]}"; do
    # Find process using the port (works on macOS and Linux)
    if command -v lsof &> /dev/null; then
        pid=$(lsof -ti :$port)
        if [ ! -z "$pid" ]; then
            echo -e "${GREEN}Found process on port $port (PID: $pid), stopping...${NC}"
            kill $pid 2>/dev/null
        else
            echo -e "${RED}No process found on port $port${NC}"
        fi
    else
        echo -e "${RED}lsof command not found, can't check port $port${NC}"
    fi
done

# Double-check for any remaining dotnet watch processes
watch_pids=$(pgrep -f "dotnet watch")
if [ ! -z "$watch_pids" ]; then
    echo -e "${GREEN}Cleaning up remaining watch processes (PIDs: $watch_pids)${NC}"
    kill $watch_pids 2>/dev/null
fi

echo -e "${GREEN}All services stopped${NC}"

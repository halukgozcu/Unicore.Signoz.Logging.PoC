#!/bin/bash

# Test direct to Elasticsearch
echo "Testing direct Elasticsearch connectivity..."
curl -X POST "http://localhost:9200/test-index/_doc" \
  -H 'Content-Type: application/json' \
  -d '{"message": "Test log directly to Elasticsearch", "timestamp": "'"$(date -u +"%Y-%m-%dT%H:%M:%S.%3NZ")"'"}' \
  | jq .

# Test OTLP HTTP endpoint for logs
echo "Testing OTLP HTTP endpoint for logs..."
curl -X POST http://localhost:5318/v1/logs \
  -H "Content-Type: application/json" \
  -d '{
    "resourceLogs": [{
      "resource": {
        "attributes": [{
          "key": "service.name",
          "value": {"stringValue": "test-service"}
        }]
      },
      "scopeLogs": [{
        "scope": {},
        "logRecords": [{
          "timeUnixNano": "'$(date +%s%N)'",
          "severityNumber": 9,
          "severityText": "INFO",
          "body": {"stringValue": "This is a test log from curl"},
          "attributes": [{
            "key": "test.attribute",
            "value": {"stringValue": "test-value"}
          }]
        }]
      }]
    }]
  }'

# Test OTLP HTTP endpoint for traces
echo "Testing OTLP HTTP endpoint for traces..."
curl -X POST http://localhost:5318/v1/traces \
  -H "Content-Type: application/json" \
  -d '{
    "resourceSpans": [{
      "resource": {
        "attributes": [{
          "key": "service.name",
          "value": {"stringValue": "test-service"}
        }]
      },
      "scopeSpans": [{
        "scope": {},
        "spans": [{
          "traceId": "'$(openssl rand -hex 16)'",
          "spanId": "'$(openssl rand -hex 8)'",
          "name": "test-span",
          "kind": 1,
          "startTimeUnixNano": "'$(date +%s%N)'",
          "endTimeUnixNano": "'$(date +%s%N)'",
          "attributes": [{
            "key": "test.attribute",
            "value": {"stringValue": "test-value"}
          }]
        }]
      }]
    }]
  }'

echo "Check indices in Elasticsearch:"
curl -X GET 'http://localhost:9200/_cat/indices?v'

echo -e "\nSearch logs:"
echo "curl -X GET 'http://localhost:9200/logs-otel-test-service-$(date +%Y.%m)/_search?pretty'"

echo -e "\nSearch traces:"
echo "curl -X GET 'http://localhost:9200/traces-otel-test-service-$(date +%Y.%m)/_search?pretty'"

#!/bin/bash

# AlUmmahNowTools Test Coverage Script
# This script runs all tests and generates comprehensive coverage reports

echo "?? Starting AlUmmahNowTools Test Coverage Analysis..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if we're in the right directory
if [ ! -f "AlUmmahNow.sln" ]; then
    print_error "Must be run from the solution root directory"
    exit 1
fi

# Create test results directory
TEST_RESULTS_DIR="TestResults"
COVERAGE_DIR="$TEST_RESULTS_DIR/Coverage"
mkdir -p "$COVERAGE_DIR"

print_status "Cleaning previous test results..."
rm -rf "$TEST_RESULTS_DIR"/*

print_status "Restoring NuGet packages..."
dotnet restore

print_status "Building solution in Release mode..."
dotnet build --configuration Release --no-restore

if [ $? -ne 0 ]; then
    print_error "Build failed. Cannot proceed with testing."
    exit 1
fi

print_success "Build completed successfully"

print_status "Running tests with coverage collection..."

# Run tests with coverage
dotnet test AlUmmahNow.Tests/AlUmmahNow.Tests.csproj \
    --configuration Release \
    --no-build \
    --verbosity normal \
    --settings AlUmmahNow.Tests/coverage.runsettings \
    --collect:"XPlat Code Coverage" \
    --results-directory "$TEST_RESULTS_DIR" \
    --logger "trx;LogFileName=TestResults.trx" \
    --logger "html;LogFileName=TestResults.html"

TEST_EXIT_CODE=$?

if [ $TEST_EXIT_CODE -ne 0 ]; then
    print_error "Some tests failed. Check the test results for details."
else
    print_success "All tests passed!"
fi

print_status "Processing coverage reports..."

# Find the coverage file
COVERAGE_FILE=$(find "$TEST_RESULTS_DIR" -name "coverage.cobertura.xml" | head -1)

if [ -z "$COVERAGE_FILE" ]; then
    print_warning "Coverage file not found. Trying alternative locations..."
    COVERAGE_FILE=$(find "$TEST_RESULTS_DIR" -name "*.cobertura.xml" | head -1)
fi

if [ -n "$COVERAGE_FILE" ]; then
    print_success "Coverage file found: $COVERAGE_FILE"
    
    # Copy coverage files to a well-known location
    cp "$COVERAGE_FILE" "$COVERAGE_DIR/coverage.cobertura.xml"
    
    # Find and copy HTML report if it exists
    HTML_REPORT=$(find "$TEST_RESULTS_DIR" -name "index.html" | head -1)
    if [ -n "$HTML_REPORT" ]; then
        HTML_DIR=$(dirname "$HTML_REPORT")
        cp -r "$HTML_DIR"/* "$COVERAGE_DIR/"
        print_success "HTML coverage report copied to $COVERAGE_DIR"
    fi
    
    # Generate coverage summary using reportgenerator if available
    if command -v reportgenerator &> /dev/null; then
        print_status "Generating comprehensive coverage report..."
        reportgenerator \
            "-reports:$COVERAGE_FILE" \
            "-targetdir:$COVERAGE_DIR/Report" \
            "-reporttypes:Html;HtmlSummary;Badges;TextSummary" \
            "-title:AlUmmahNowTools Coverage Report" \
            "-tag:$(date +%Y%m%d_%H%M%S)"
        
        if [ $? -eq 0 ]; then
            print_success "Comprehensive coverage report generated in $COVERAGE_DIR/Report"
        fi
    else
        print_warning "ReportGenerator not found. Install with: dotnet tool install -g dotnet-reportgenerator-globaltool"
    fi
    
    # Extract coverage percentage using grep/awk if possible
    if command -v grep &> /dev/null && command -v awk &> /dev/null; then
        COVERAGE_PERCENT=$(grep 'line-rate' "$COVERAGE_FILE" | head -1 | awk -F'"' '{print $2}' | awk '{printf "%.1f", $1*100}')
        if [ -n "$COVERAGE_PERCENT" ]; then
            print_success "Overall test coverage: ${COVERAGE_PERCENT}%"
            
            # Check if we achieved our target
            COVERAGE_INT=$(echo "$COVERAGE_PERCENT" | cut -d'.' -f1)
            if [ "$COVERAGE_INT" -ge 90 ]; then
                print_success "?? Excellent coverage! Target of 90%+ achieved."
            elif [ "$COVERAGE_INT" -ge 80 ]; then
                print_warning "??  Good coverage, but aim for 90%+ (current: ${COVERAGE_PERCENT}%)"
            else
                print_error "? Coverage below 80% (current: ${COVERAGE_PERCENT}%). More tests needed."
            fi
        fi
    fi
else
    print_error "Coverage file not found. Coverage collection may have failed."
fi

print_status "Generating test summary..."

# Create a summary file
SUMMARY_FILE="$TEST_RESULTS_DIR/TestSummary.txt"
cat > "$SUMMARY_FILE" << EOF
AlUmmahNowTools Test Coverage Report
====================================
Generated: $(date)
Test Exit Code: $TEST_EXIT_CODE

Test Results:
- TRX Report: TestResults.trx
- HTML Report: TestResults.html

Coverage Reports:
- Cobertura XML: $COVERAGE_DIR/coverage.cobertura.xml
- HTML Report: $COVERAGE_DIR/index.html (if available)
- Comprehensive Report: $COVERAGE_DIR/Report/index.html (if ReportGenerator available)

To view HTML coverage report:
1. Open $COVERAGE_DIR/index.html in a web browser
2. Or open $COVERAGE_DIR/Report/index.html for comprehensive report

To improve coverage:
1. Review uncovered code in the HTML report
2. Add tests for missing scenarios
3. Consider adding integration tests for complex workflows
4. Ensure edge cases and error conditions are tested

Coverage Target: 100% (minimum 90% recommended)
EOF

print_success "Test summary written to $SUMMARY_FILE"

print_status "Test coverage analysis complete!"
echo ""
echo "?? Results Summary:"
echo "  - Test Results: $TEST_RESULTS_DIR/"
echo "  - Coverage Reports: $COVERAGE_DIR/"
echo "  - Summary: $SUMMARY_FILE"
echo ""

if [ $TEST_EXIT_CODE -eq 0 ]; then
    print_success "? All tests passed! Review coverage reports to identify areas for improvement."
else
    print_error "? Some tests failed. Fix failing tests before analyzing coverage."
fi

exit $TEST_EXIT_CODE
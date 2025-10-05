@echo off
REM AlUmmahNowTools Test Coverage Script for Windows
REM This script runs all tests and generates comprehensive coverage reports

echo ?? Starting AlUmmahNowTools Test Coverage Analysis...

REM Check if we're in the right directory
if not exist "AlUmmahNow.sln" (
    echo [ERROR] Must be run from the solution root directory
    exit /b 1
)

REM Create test results directory
set TEST_RESULTS_DIR=TestResults
set COVERAGE_DIR=%TEST_RESULTS_DIR%\Coverage
if exist "%TEST_RESULTS_DIR%" rmdir /s /q "%TEST_RESULTS_DIR%"
mkdir "%COVERAGE_DIR%"

echo [INFO] Cleaning previous test results...

echo [INFO] Restoring NuGet packages...
dotnet restore
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Package restore failed
    exit /b 1
)

echo [INFO] Building solution in Release mode...
dotnet build --configuration Release --no-restore
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Build failed. Cannot proceed with testing.
    exit /b 1
)

echo [SUCCESS] Build completed successfully

echo [INFO] Running tests with coverage collection...

REM Run tests with coverage
dotnet test AlUmmahNow.Tests/AlUmmahNow.Tests.csproj ^
    --configuration Release ^
    --no-build ^
    --verbosity normal ^
    --settings AlUmmahNow.Tests/coverage.runsettings ^
    --collect:"XPlat Code Coverage" ^
    --results-directory "%TEST_RESULTS_DIR%" ^
    --logger "trx;LogFileName=TestResults.trx" ^
    --logger "html;LogFileName=TestResults.html"

set TEST_EXIT_CODE=%ERRORLEVEL%

if %TEST_EXIT_CODE% neq 0 (
    echo [ERROR] Some tests failed. Check the test results for details.
) else (
    echo [SUCCESS] All tests passed!
)

echo [INFO] Processing coverage reports...

REM Find the coverage file
for /r "%TEST_RESULTS_DIR%" %%f in (coverage.cobertura.xml) do (
    set COVERAGE_FILE=%%f
    goto :found_coverage
)

for /r "%TEST_RESULTS_DIR%" %%f in (*.cobertura.xml) do (
    set COVERAGE_FILE=%%f
    goto :found_coverage
)

echo [WARNING] Coverage file not found
goto :no_coverage

:found_coverage
echo [SUCCESS] Coverage file found: %COVERAGE_FILE%

REM Copy coverage files to a well-known location
copy "%COVERAGE_FILE%" "%COVERAGE_DIR%\coverage.cobertura.xml" >nul

REM Find and copy HTML report if it exists
for /r "%TEST_RESULTS_DIR%" %%f in (index.html) do (
    set HTML_REPORT=%%f
    for %%d in ("%%~dpf.") do set HTML_DIR=%%~fd
    xcopy "%HTML_DIR%\*" "%COVERAGE_DIR%\" /e /q >nul
    echo [SUCCESS] HTML coverage report copied to %COVERAGE_DIR%
    goto :check_reportgen
)

:check_reportgen
REM Generate coverage summary using reportgenerator if available
where reportgenerator >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo [INFO] Generating comprehensive coverage report...
    reportgenerator ^
        "-reports:%COVERAGE_FILE%" ^
        "-targetdir:%COVERAGE_DIR%\Report" ^
        "-reporttypes:Html;HtmlSummary;Badges;TextSummary" ^
        "-title:AlUmmahNowTools Coverage Report" ^
        "-tag:%date:~-4,4%%date:~-10,2%%date:~-7,2%_%time:~0,2%%time:~3,2%%time:~6,2%"
    
    if %ERRORLEVEL% equ 0 (
        echo [SUCCESS] Comprehensive coverage report generated in %COVERAGE_DIR%\Report
    )
) else (
    echo [WARNING] ReportGenerator not found. Install with: dotnet tool install -g dotnet-reportgenerator-globaltool
)

:no_coverage

echo [INFO] Generating test summary...

REM Create a summary file
set SUMMARY_FILE=%TEST_RESULTS_DIR%\TestSummary.txt
(
echo AlUmmahNowTools Test Coverage Report
echo ====================================
echo Generated: %date% %time%
echo Test Exit Code: %TEST_EXIT_CODE%
echo.
echo Test Results:
echo - TRX Report: TestResults.trx
echo - HTML Report: TestResults.html
echo.
echo Coverage Reports:
echo - Cobertura XML: %COVERAGE_DIR%\coverage.cobertura.xml
echo - HTML Report: %COVERAGE_DIR%\index.html ^(if available^)
echo - Comprehensive Report: %COVERAGE_DIR%\Report\index.html ^(if ReportGenerator available^)
echo.
echo To view HTML coverage report:
echo 1. Open %COVERAGE_DIR%\index.html in a web browser
echo 2. Or open %COVERAGE_DIR%\Report\index.html for comprehensive report
echo.
echo To improve coverage:
echo 1. Review uncovered code in the HTML report
echo 2. Add tests for missing scenarios
echo 3. Consider adding integration tests for complex workflows
echo 4. Ensure edge cases and error conditions are tested
echo.
echo Coverage Target: 100%% ^(minimum 90%% recommended^)
) > "%SUMMARY_FILE%"

echo [SUCCESS] Test summary written to %SUMMARY_FILE%

echo [INFO] Test coverage analysis complete!
echo.
echo ?? Results Summary:
echo   - Test Results: %TEST_RESULTS_DIR%\
echo   - Coverage Reports: %COVERAGE_DIR%\
echo   - Summary: %SUMMARY_FILE%
echo.

if %TEST_EXIT_CODE% equ 0 (
    echo [SUCCESS] ? All tests passed! Review coverage reports to identify areas for improvement.
) else (
    echo [ERROR] ? Some tests failed. Fix failing tests before analyzing coverage.
)

exit /b %TEST_EXIT_CODE%
---
name: Maintain Content Parsers
version: 1.0.0
description: Maintain and update content parsers for news sites by running tests, updating test data, and verifying parser correctness
tags:
  - content-parsers
  - testing
  - maintenance
  - newsfeed

## Overview
This prompt helps maintain and update content parsers for news sites in the AgitpropScraper project. It provides a systematic approach to

1. Run online content parser tests to identify failures
2. For failed sites, fetch current HTML content from the web
3. Save the HTML to testdata directories
4. Update content parsers based on new test data
5. Run online tests again to verify correctness
6. Use git for version control at meaningful steps

## Usage
This prompt is designed to be used in a VS Code workspace with the AgitpropScraper repository. It can be invoked by:

1. Opening the prompt in VS Code
2. Running the prompt with the appropriate context
3. Following the step-by-step guidance provided

## Steps

### Step 1: Run Online Tests
First, run the online content parser tests to identify which sites are failing:

```bash
First run `dotnet build` and confirm it exits with code 0. If the build fails, fix compilation errors before interpreting any test output. Only proceed to parse test results after a clean build.
dotnet test --filter "FullyQualifiedName~ContentParserOnlineTests"
```

### Step 2: Identify Failed Sites
Review the test output to identify which news sites are failing. Note the site names and any error messages.

### Step 3: Fetch Current HTML for Failed Sites
For each failed site:

1. Navigate to the site in a browser
2. Copy the full HTML content
3. Save it to the appropriate testdata directory:
   - Location: `Agitprop.Sinks.Newsfeed_Test/TestData/{siteName}/1.html`
   - Replace existing HTML file with the current version

### Step 4: Update Content Parsers
Based on the new test data, update the corresponding content parser:

1. Open the content parser file for the site
2. Review the parsing logic
3. Update the parser to correctly extract content from the new HTML structure
4. Test the updated parser with the new test data

### Step 5: Run Offline Tests
Run the offline content parser tests to verify your updates:

```bash
First run `dotnet build` and confirm it exits with code 0. If the build fails, fix compilation errors before interpreting any test output. Only proceed to parse test results after a clean build.
dotnet test --filter "FullyQualifiedName~ContentParserOfflineTests"
```

### Step 6: Run Online Tests Again
Run the online tests again to verify that all parsers are working correctly:

```bash
First run `dotnet build` and confirm it exits with code 0. If the build fails, fix compilation errors before interpreting any test output. Only proceed to parse test results after a clean build.
dotnet test --filter "FullyQualifiedName~ContentParserOnlineTests"
```

If Step 6 reveals a site that was passing before Step 1 but is now failing, stop and do not commit. Run `git diff` to identify which parser change introduced the regression, revert that specific file with `git checkout -- <file>`, and re-run offline and online tests before proceeding.

If online tests still fail after two full iterations of Steps 3–6 for the same site, stop iterating for that site and add a comment in the parser file: // TODO: Parser broken as of {date} — site structure may require manual inspection. Commit the working sites separately and log the failing site name for human follow-up.

### Step 7: Commit Changes
Use git to commit your changes at meaningful steps:

1. After updating test data: `git add Agitprop.Sinks.Newsfeed_Test/TestData/{siteName}/`
2. After updating parsers: `git add Agitprop.Sinks.Newsfeed.Scrapers.ContentParsers/{SiteName}ArticleContentParser.cs`
3. After all changes are complete: 
   - If updating a single site: `git commit -m "Update content parser for {siteName}: refresh HTML fixture and parser logic"`
   - If updating multiple sites in one pass: `git commit -m "Refresh HTML fixtures and parsers for: {site1}, {site2}, ..."`

## Definitions

In all commands below, {siteName} is the lowercase directory name as it appears under Agitprop.Sinks.Newsfeed_Test/TestData/ (e.g., bbc-news). {SiteName} is the PascalCase class name prefix as it appears in the ContentParsers directory (e.g., BbcNews). Derive both values from the failing test name shown in Step 2 output.

## Example Invocation

To use this prompt, you would typically:

1. Start with running the online tests
2. Follow the steps above for any failed sites
3. Repeat until all tests pass
4. Commit your changes

## Tips

- Always run offline tests before committing to ensure your parser changes work correctly
- Use git diff to review changes before committing
- Keep a record of which sites you've updated
- Test one site at a time to isolate issues
- If a parser needs significant changes, consider creating a backup first

## Troubleshooting

If tests continue to fail:

1. Check that the HTML files are correctly saved
2. Verify that the testcases.json files are properly formatted
3. Review the parser logic against the new HTML structure
4. Run tests with more verbose output to see specific failures
5. Consider using browser developer tools to inspect the current site structure
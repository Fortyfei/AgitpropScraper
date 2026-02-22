# AgitpropScraper

AgitpropScraper is a comprehensive scraping and processing framework designed to collect, parse, and analyze data from various sources. The project is modular, with distinct components for scraping, data processing, and natural language processing (NLP).

## Project Structure

The repository is organized into the following main components:

- **Agitprop.AppHost**: Hosts the main application logic and configuration files.
- **Agitprop.ConsoleToolKit**: Provides command-line tools for managing scraping jobs and retrying failed tasks.
- **Agitprop.Consumer**: Handles data consumption and processing pipelines.
- **Agitprop.Core**: Contains core abstractions, models, and utilities used across the project.
- **Agitprop.Infrastructure**: Implements infrastructure-level services like page loading and proxy management.
- **Agitprop.Infrastructure.Puppeteer**: Integrates Puppeteer for advanced web scraping capabilities.
- **Agitprop.NLPService**: A Python-based service for natural language processing, including named entity recognition.
- **Agitprop.RssFeedReader**: Reads and processes RSS feeds.
- **Agitprop.Scraper.Sinks.Newsfeed**: Processes and stores scraped newsfeed data.
- **Agitprop.ServiceDefaults**: Provides default configurations and shared services.

## Key Features

- **Web Scraping**: Supports scraping with Puppeteer and custom proxy management.
- **NLP Integration**: Includes a Python-based NLP service for advanced text analysis.
- **Command-Line Tools**: Offers tools for managing scraping jobs and retries.
- **Modular Design**: Each component is self-contained and reusable.

## Getting Started

### Prerequisites

- .NET SDK
- Python 3.x
- Docker (for containerized services)

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/your-repo/AgitpropScraper.git
   ```
2. Navigate to the project directory:
   ```bash
   cd AgitpropScraper
   ```
3. Install dependencies for the NLP service:
   ```bash
   cd Agitprop.NLPService
   pip install -r requirements.txt
   ```

### Running the Project

1. Build the solution:
   ```bash
   dotnet build Agitprop.Scraper.sln
   ```
2. Run the main application:
   ```bash
   dotnet run --project Agitprop.AppHost
   ```
3. Start the NLP service:
   ```bash
   cd Agitprop.NLPService
   python app.py
   ```

## Container publishing and CI

Docker images for every service are published to GitHub Container Registry (GHCR) using two manual workflows:

- **Release** (`.github/workflows/release-publish.yml`): run by dispatch with a `version` input. Images are built for `linux/amd64,linux/arm64` using `docker buildx bake`, tagged with the supplied semver (a leading `v` is added if missing) and `latest`, pushed under `ghcr.io/${{ github.repository_owner }}/<service>`, and a git tag is added to the commit.

- **Dev** (`.github/workflows/dev-publish.yml`): run by dispatch with a `branch` (and optional `tag-suffix`). The specified branch is checked out, images are built and pushed to `ghcr.io/${{ github.repository_owner }}/<service>-dev` with a branch/sha-based tag and `latest`. The workflow also attempts to mark the package private and set a 90‑day retention via the GitHub API.

Both workflows rely on `docker-bake.hcl` at the repository root to describe the available build targets; new services can be added by creating a corresponding `target` and adding the slug to the service list in the workflows.

No additional secrets are required for repository-owned images; the runners log in with `${{ secrets.GITHUB_TOKEN }}`. If you need to push to a different namespace or an organization where `GITHUB_TOKEN` lacks permission, create a Personal Access Token with `write:packages` (and `repo` if private) and store it as a secret, then adjust the login steps.

Local testing can be done by invoking `docker buildx bake <target>` from the repo root or by copying the build loops used in the workflows.

## Contact

For questions or support, please contact [your-email@example.com].
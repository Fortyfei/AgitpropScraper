# BI Frontend Requirements

## Overview

Build a Blazor-based analytics dashboard in `Agitprop.Web.Client` that helps users explore relationships between entities mentioned in news articles. The app should be data-driven, interactive, and polished in a modern BI style.

## Pages

### Dashboard
- Top entities bar chart
- Entity type distribution donut chart
- Mentions-over-time line chart
- KPI summary cards
- Date range and entity type filter bar
- Drill-down entity navigation from charts

### Entity Explorer
- Searchable and filterable list of entities
- Columns: name, type, mention count
- Click-to-detail navigation

### Entity Detail
- Selected entity trend line
- Related entities list
- Mentioning article list
- Summary card with total mentions and type

### Network Graph
- Force-directed graph of entities
- Node size = mention count
- Edge thickness = co-occurrence strength
- Color by entity type
- Clickable nodes that navigate to details

### Trends
- Multi-line comparison chart for selected entities
- Color-coded series legend
- Hover tooltips

### Articles
- Table of articles with title, published date, and mentioned entities
- Article row actions for open or entity inspection

## Layout and Navigation

- Fixed left sidebar navigation
- Top page header and filter bar for analytics pages
- Responsive design with stacked cards on smaller screens
- Consistent color and typography across pages

## Data Requirements

- Entity summary data with type and mention count
- Entity mention timeline data
- Co-occurrence / related entity data
- Article metadata and mentions
- Entity type distribution data

## API Requirements

Add or enhance endpoints in `Agitprop.Web.Api` for:
- Paginated entity list and search
- Entity details and timeline
- Related entity co-occurrence
- Trending entities and comparison series
- Article list by entity and date range
- Entity autocomplete and type distribution

## Implementation Stories

1. MVP Dashboard and Drill-Down
   - Add sidebar navigation and routes
   - Build dashboard page with core charts and filters
   - Add entity explorer and entity detail pages
   - Wire click navigation from charts to detail pages

2. Entity Search and Browsing
   - Add entity explorer search and filter controls
   - Add article list page and article metadata support
   - Enhance entity detail page with related entities and articles

3. Reusable Analytics Components
   - Create shared filter bar, cards, chips, and table components
   - Add reusable chart wrappers for bar/donut/line views
   - Standardize entity type color and badge styles

4. Network Graph and Trend Comparison
   - Add force-directed network graph page
   - Add multi-entity trend comparison page
   - Add hover tooltips and node/edge interactions

5. Full BI Polish
   - Add polished responsive layout, spacing, and hover states
   - Add table sorting, paging, and advanced legend controls
   - Refine colors, typography, and accessibility
   - Complete cross-page consistency QA

## Verification Criteria

- Dashboard renders expected chart sections
- Entity drill-down works from charts, tables, and graph nodes
- Network graph is interactive and clickable
- Trend comparison page supports multiple entities
- Articles table displays article titles and mention details
- Filters update page contents correctly
- UI works on desktop and narrow viewports

# Agitprop.Infrastructure.Postgres

## Purpose

A shared library for **PostgreSQL-specific infrastructure** used across
Agitprop.Scraper.Consumer and Agitprop.Web.Api.

## Contents

This project is a thin data-access layer shared by the sink and API layers.
It currently contains:

- EF Core model configurations specific to PostgreSQL (Npgsql).
- Shared database access utilities.

## Usage

Referenced by `Agitprop.Sinks.Newsfeed` (which contains the EF Core
`AppDbContext`). The actual `DbContext` and repositories live in
`Agitprop.Sinks.Newsfeed/Database/`.

## Relationship to AppDbContext

- `AppDbContext.cs` (in `Agitprop.Sinks.Newsfeed/Database/`) extends
  `DbContext` and maps `PostgresArticle`, `PostgresEntity`, `PostgresMention`.
- `Agitprop.Infrastructure.Postgres` provides any PostgreSQL-specific
  extensions or Npgsql type handlers needed by the shared context.

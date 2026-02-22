# Docker Bake file describing each service build target.
# Each target uses repository root as context because the Dockerfiles
# reference files relative to repo root.

group "all" {
    targets = ["agitprop-scraper-rssfeedreader", "agitprop-scraper-nlpservice", "agitprop-web-api", "agitprop-scraper-consumer", "agitprop-web-client"]
}

# service slugs are derived from the folder names by lowercasing and
# replacing any non-alphanumeric characters with dashes.  The workflows
# build and push using the same slugs, with "-dev" appended for dev images.

target "agitprop-scraper-rssfeedreader" {
    context = "."
    dockerfile = "Agitprop.Scraper.RssFeedReader/Dockerfile"
    tags = []
}

target "agitprop-scraper-nlpservice" {
    context = "."
    dockerfile = "Agitprop.Scraper.NLPService/Dockerfile"
    tags = []
}

target "agitprop-web-api" {
    context = "."
    dockerfile = "Agitprop.Web.API/Dockerfile"
    tags = []
}

target "agitprop-scraper-consumer" {
    context = "."
    dockerfile = "Agitprop.Scraper.Consumer/Dockerfile"
    tags = []
}

target "agitprop-web-client" {
    context = "."
    dockerfile = "Agitprop.Web.Client/Dockerfile"
    tags = []
}

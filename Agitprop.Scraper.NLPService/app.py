# Python

"""
FastAPI-based NLP service for named entity recognition (NER) using spaCy.
"""

from contextvars import ContextVar
from fastapi import FastAPI, HTTPException, Body, Request
from pydantic import BaseModel
from typing import List
import logging
import os
import time
import uuid
import spacy
from opentelemetry import metrics
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource, SERVICE_NAME
from opentelemetry.exporter.otlp.proto.grpc.metric_exporter import OTLPMetricExporter


REQUEST_ID_CTX: ContextVar[str] = ContextVar("request_id", default="-")


class RequestContextFilter(logging.Filter):
    def filter(self, record: logging.LogRecord) -> bool:
        record.request_id = REQUEST_ID_CTX.get()
        return True


def configure_logging() -> logging.Logger:
    log_level = os.environ.get("LOG_LEVEL", "INFO").upper()
    formatter = logging.Formatter(
        fmt="%(asctime)s %(levelname)s [%(name)s] [req=%(request_id)s] %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    root_logger = logging.getLogger()
    root_logger.setLevel(log_level)

    if not root_logger.handlers:
        handler = logging.StreamHandler()
        root_logger.addHandler(handler)

    context_filter = RequestContextFilter()
    for handler in root_logger.handlers:
        handler.setFormatter(formatter)
        handler.addFilter(context_filter)

    app_logger = logging.getLogger("agitprop.nlpservice")
    app_logger.info("Logging configured")
    return app_logger


def configure_metrics() -> metrics.Histogram:
    service_name = os.environ.get("OTEL_SERVICE_NAME", "nlpservice")
    resource = Resource.create({SERVICE_NAME: service_name})
    exporter = OTLPMetricExporter()
    reader = PeriodicExportingMetricReader(exporter)
    provider = MeterProvider(resource=resource, metric_readers=[reader])
    metrics.set_meter_provider(provider)
    meter = metrics.get_meter("agitprop.nlpservice")
    return meter.create_histogram(
        name="http.server.request.duration",
        description="Duration of HTTP requests in seconds",
        unit="s",
    )


app = FastAPI(title="NLP Service", description="Named Entity Recognition API using spaCy")
logger = configure_logging()
http_request_duration = configure_metrics()

try:
    model_load_start = time.perf_counter()
    logger.info("Loading spaCy model 'hu_core_news_lg'")
    nlp = spacy.load("hu_core_news_lg")
    model_load_ms = int((time.perf_counter() - model_load_start) * 1000)
    logger.info("spaCy model loaded successfully in %d ms", model_load_ms)
except Exception as e:
    logger.exception("Failed to load spaCy model: %s", str(e))
    raise


@app.middleware("http")
async def log_requests(request: Request, call_next):
    request_id = request.headers.get("x-request-id") or str(uuid.uuid4())
    token = REQUEST_ID_CTX.set(request_id)
    started = time.perf_counter()

    logger.info("Incoming request: %s %s", request.method, request.url.path)

    try:
        response = await call_next(request)
        elapsed_s = time.perf_counter() - started
        elapsed_ms = int(elapsed_s * 1000)
        response.headers["x-request-id"] = request_id
        http_request_duration.record(
            elapsed_s,
            attributes={
                "http.request.method": request.method,
                "http.route": request.url.path,
                "http.response.status_code": response.status_code,
            },
        )
        logger.info(
            "Completed request: %s %s -> %d in %d ms",
            request.method,
            request.url.path,
            response.status_code,
            elapsed_ms,
        )
        return response
    except Exception:
        elapsed_s = time.perf_counter() - started
        http_request_duration.record(
            elapsed_s,
            attributes={
                "http.request.method": request.method,
                "http.route": request.url.path,
                "http.response.status_code": 500,
            },
        )
        logger.exception(
            "Unhandled error for request: %s %s after %d ms",
            request.method,
            request.url.path,
            int(elapsed_s * 1000),
        )
        raise
    finally:
        REQUEST_ID_CTX.reset(token)


@app.get("/health")
def healthcheck():
    logger.debug("Health check requested")
    return {"status": "alive"}


class AnalyzeRequest(BaseModel):
    text: str


@app.post("/analyzeSingle")
def analyze_single_corpus(req: AnalyzeRequest = Body(...)):
    started = time.perf_counter()
    text_len = len(req.text or "")
    logger.info("Analyzing single corpus (text_length=%d)", text_len)

    try:
        doc = nlp(req.text)
        result = get_named_entities(doc)
        elapsed_ms = int((time.perf_counter() - started) * 1000)
        logger.info(
            "Single corpus analyzed successfully (entities=%d, duration_ms=%d)",
            len(result),
            elapsed_ms,
        )
        return result
    except Exception as e:
        logger.exception("analyzeSingle failed: %s", str(e))
        raise HTTPException(status_code=500, detail=str(e))


class AnalyzeBatchRequest(BaseModel):
    texts: List[str]


@app.post("/analyzeBatch")
def analyze_batch_corpus(req: AnalyzeBatchRequest = Body(...)):
    started = time.perf_counter()
    batch_size = len(req.texts)
    logger.info("Analyzing batch corpus (items=%d)", batch_size)

    try:
        result = []

        for doc in nlp.pipe(req.texts):
            entities = get_named_entities(doc)
            result.append(entities)

        total_entities = sum(len(entities) for entities in result)
        elapsed_ms = int((time.perf_counter() - started) * 1000)
        logger.info(
            "Batch corpus analyzed successfully (items=%d, total_entities=%d, duration_ms=%d)",
            batch_size,
            total_entities,
            elapsed_ms,
        )
        return result
    except Exception as e:
        logger.exception("analyzeBatch failed: %s", str(e))
        raise HTTPException(status_code=500, detail=str(e))


def get_named_entities(doc):
    entities = []
    seen = set()
    for ent in doc.ents:
        entity_dict = {
            "Item1": ent.lemma_,
            "Item2": ent.label_
        }
        key = (entity_dict["Item1"], entity_dict["Item2"])
        if key not in seen:
            seen.add(key)
            entities.append(entity_dict)
    return entities


@app.get("/discovery")
def discovery():
    logger.debug("Service discovery requested")
    return {"endpoints": ["/health", "/analyzeSingle", "/analyzeBatch", "/discovery"]}


if __name__ == '__main__':
    import uvicorn

    port = int(os.environ.get('PORT', 8111))
    reload = bool(os.environ.get('RELOAD', "False"))
    logLevel = os.environ.get('LOG_LEVEL', 'trace')
    host = os.environ.get('HOST', '127.0.0.1')

    uvicorn.run(app, host=host, port=port, reload=reload, log_level=logLevel)

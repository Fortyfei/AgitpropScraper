# Agitprop.Scraper.NLPService

## Purpose

A .NET class library that performs Hungarian named-entity recognition in-process
for the scraper consumer. `NamedEntityRecognizer` implements
`INamedEntityRecognizer` and runs the NYTK NerKor huBERT token-classification
model with ONNX Runtime.

## Entity output

The model recognizes `PER`, `LOC`, `ORG`, and `MISC` labels. It returns entity
surface text from the original article, preserves first-seen order, and removes
duplicate `(name, type)` pairs. The model does not provide spaCy-style lemmas;
surface forms are an intentional compatibility difference.

Single-text and batch calls retain the existing `NamedEntityCollection` result
shape. Inputs longer than the model's 512-token limit are processed in
overlapping windows.

## Model assets and offline deployment

The [ONNX export](https://huggingface.co/foltin/nerkor-hubert-hungarian-onnx)
and [upstream NYTK model](https://huggingface.co/NYTK/named-entity-recognition-nerkor-hubert-hungarian)
declare Apache-2.0 licensing; the underlying
[NerKor corpus](https://github.com/nytud/NYTK-NerKor) is CC BY-SA 4.0, so review
provenance and distribution obligations for the intended deployment. The export
is fetched from a pinned revision.

`NerModelProvisioner` self-provisions the model on first use: when
`NamedEntityRecognizer` starts, it downloads `model.onnx`, `tokenizer.json`,
and `config.json` from the pinned Hugging Face revision into the model
directory (unless the files already exist there) and validates their SHA-256
hashes before loading them. No separate script or CI provisioning step is
required — running the consumer (via `aspire run`, `dotnet run`, or a
published container) is enough to provision the model automatically. The
ONNX artifact is about 440 MB and is ignored by Git.

Override the model directory with `NLP__ModelDirectory`; by default, the
recognizer downloads to and loads from `ner-model` beside the consumer
binaries. Startup fails explicitly if any required model asset cannot be
downloaded or fails checksum validation. To pre-seed the model directory
(for example, in an offline/air-gapped environment), place correctly-named,
checksum-matching files at that path before starting the consumer; the
provisioner will detect and reuse them without downloading.

## Validation

`Agitprop.UnitTests/NamedEntityRecognizerTests.cs` tests wordpiece alignment,
surface-form extraction, labels, ordering, de-duplication, and batch shape
without loading the large model. An explicit model-backed smoke test runs the
offline article text in `Agitprop.Sinks.Newsfeed_Test/TestData`; use this corpus
for quality qualification against a spaCy baseline before production cutover.

# G0-07 AI and speech-to-text provider proposal

**Status** Proposed for team review on 19 November 2026. This proposal addresses
requirement extraction and consented Tamil and English transcription in
REQVIORA Input Studio. [Project decision record](https://github.com/sasikarankobiga/reqviora-backend/blob/docs/second-brain/docs/second-brain/open-questions.md)

## Recommended pilot

Pilot OpenAI `gpt-6.1-terra` through the Responses API for structured requirement
extraction and `gpt-transcribe` through the file transcription endpoint for
editable transcripts. The pair uses one API provider and supports schema-guided
text extraction and completed audio files. Keep `en`, `en-LK`, `ta`, and `ta-LK`
in REQVIORA and verify the provider's language hints and accuracy with Sri Lankan
Tamil and English speech. [Text model](https://developers.openai.com/api/docs/models/gpt-6.1-terra) · [File transcription](https://developers.openai.com/api/docs/guides/speech-to-text)

If Tamil accuracy needs a comparison, benchmark Google Cloud Speech-to-Text V1
`default` against the same recordings. Google explicitly lists `ta-LK` for that
model. This option adds a second provider account and its processing region,
retention, and billing need their own review. [Google language support](https://docs.cloud.google.com/speech-to-text/docs/v1/speech-to-text-supported-languages) · [Google data logging](https://docs.cloud.google.com/speech-to-text/docs/v1/data-logging)

OpenAI lists shutdown dates of 11 December 2026 for `gpt-5-mini` and
26 February 2027 for `gpt-4o-mini-transcribe`. The recommended models are
OpenAI's named replacements for structured text and completed audio files.
[Deprecations](https://developers.openai.com/api/docs/deprecations)

## Cost and data handling

| Example monthly usage | Published rate | Estimated cost |
| --- | --- | --- |
| 1,000 extractions at 6,000 uncached input tokens and 1,500 billed output tokens each | $1 per million input and $5 per million output tokens | $13.50 |
| 300 minutes of file transcription | $0.0045 per minute | $1.35 |
| **Total model usage** | | **$14.85** |

This example uses the standard API rates checked on 19 November 2026. Budget
approval should account for retries, actual billed tokens, and applicable taxes.
Keep the existing Input Studio bounds of 25 MB and 30 minutes per clip. Record
the approved daily request and monthly spend limits in the PR. [API pricing](https://developers.openai.com/api/docs/pricing) · [Input Studio limits](../../src/Reqviora.Application/Features/Documents/InputStudioContracts.cs)

Store provider credentials in backend secrets and send only the text segments
or consented audio needed for the request. Use `store: false` and foreground
Responses API calls. OpenAI documents standard abuse-monitoring retention of up
to 30 days for Responses and lists retention as none for transcription abuse
monitoring and application state. Confirm the account's actual settings and
processing region for both endpoints before sending project data. [API data controls](https://developers.openai.com/api/docs/guides/your-data)

Proposed voice consent copy follows.

> If you continue, REQVIORA will send this recording to OpenAI for
> transcription. You can review and edit the transcript before using it.
> Manual entry is also available.

## Pilot evidence

1. Record short scripts with consenting team volunteers using fictional
   requirements. Include Sri Lankan Tamil, Sri Lankan English, and mixed
   Tamil/English clips in quiet and noisy settings. Examples include
   “வரைவை மூன்று நாட்களுக்குச் சேமிக்க வேண்டும்” and “Only the project owner
   can approve the Blueprint.” Use a human-written transcript for each clip.
2. Record word error rate, corrections to names, numbers and negations, edit
   time, API latency, and actual cost per language. Product and Engineering
   agree on target values before evaluating the results.
3. Run extraction on reference and edited transcripts and on sample document
   segments. Compare output with human-labeled requirements. Verify the
   structured output and source citations with the existing validators and
   require explicit human confirmation for every candidate.
4. Exercise provider errors, timeouts, quota exhaustion, and declined consent.
   Verify that manual entry is available and that failed regeneration keeps the
   previous draft.

## Team review

| Role | Decision to record in the PR |
| --- | --- |
| Product | Pilot scope, quality targets, and budget limits |
| Security | Consent copy, processing region, and retention settings |
| Engineering | Exact model IDs, measured bilingual results, and validation and fallback behavior |

Record the approved provider and model IDs, usage limits, data settings, and
pilot evidence in this decision once the team agrees. The provider adapter can
then use the existing application interfaces and backend configuration.

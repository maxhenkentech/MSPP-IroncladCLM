# ⚖️ Ironclad CLM Connector — Action Reference Guide

A complete reference for every action available in the Ironclad CLM custom connector for Microsoft Power Platform. This guide covers inputs, outputs, usage patterns, and practical tips for Power Automate, Power Apps, and Copilot Studio.

> 📖 For setup instructions and troubleshooting, see the main [README](README.md).

---

## 📑 Table of Contents

- [General Concepts](#general-concepts)
  - [Dynamic Dropdowns](#dynamic-dropdowns)
  - [Impersonation Headers](#impersonation-headers)
  - [Complex Data Types](#complex-data-types)
  - [Pagination](#pagination)
- [Workflow Operations](#workflow-operations)
  - [Schemas](#schemas)
    - [List all Workflow Schemas](#list-all-workflow-schemas)
    - [Retrieve a Workflow Schema](#retrieve-a-workflow-schema)
    - [Retrieve Formatted Workflow Schema](#retrieve-formatted-workflow-schema)
  - [Lifecycle](#lifecycle)
    - [Create a Workflow Synchronously](#create-a-workflow-synchronously)
    - [Create a Workflow Asynchronously](#create-a-workflow-asynchronously)
    - [Create a Workflow (Raw Body)](#create-a-workflow-raw-body)
    - [Retrieve Async Workflow Status](#retrieve-async-workflow-status)
    - [Retrieve a Workflow](#retrieve-a-workflow)
    - [List all Workflows](#list-all-workflows)
    - [List All Workflows V2](#list-all-workflows-v2)
    - [Update Workflow Metadata](#update-workflow-metadata)
    - [Cancel Workflow](#cancel-workflow)
    - [Pause Workflow](#pause-workflow)
    - [Resume Workflow](#resume-workflow)
    - [Revert to Review Step](#revert-to-review-step)
  - [Approvals](#approvals)
    - [List all Workflow Approvals](#list-all-workflow-approvals)
    - [Retrieve Approval Requests](#retrieve-approval-requests)
    - [Update Approval on a Workflow](#update-approval-on-a-workflow)
  - [Comments](#comments)
    - [Create a Comment](#create-a-comment)
    - [List all Comments](#list-all-comments)
    - [Retrieve a Comment](#retrieve-a-comment)
  - [Documents](#documents)
    - [Create a Workflow Document](#create-a-workflow-document)
    - [Create a Signed Document](#create-a-signed-document)
    - [Retrieve a Workflow Document](#retrieve-a-workflow-document)
  - [Email](#email)
    - [Retrieve Email Threads](#retrieve-email-threads)
    - [Retrieve an Email Thread](#retrieve-an-email-thread)
  - [Participants & Turn History](#participants-turn-history)
    - [List all Workflow Participants](#list-all-workflow-participants)
    - [Retrieve Turn History](#retrieve-turn-history)
- [Record Operations](#record-operations)
  - [Schemas](#record-schemas)
    - [Retrieve Record Schemas](#retrieve-record-schemas)
    - [Retrieve Formatted Record Schema](#retrieve-formatted-record-schema)
  - [CRUD](#record-crud)
    - [Create a Record](#create-a-record)
    - [Retrieve a Record](#retrieve-a-record)
    - [Update Record Metadata](#update-record-metadata)
    - [Replace a Record](#replace-a-record)
    - [Delete a Record](#delete-a-record)
  - [Querying Records](#querying-records)
    - [List All Records V2 ⭐](#list-all-records-v2)
    - [List All Records (Deprecated)](#list-all-records-deprecated)
    - [Retrieve XLSX Export](#retrieve-xlsx-export)
  - [Attachments & Signed Copies](#attachments-signed-copies)
    - [Create an Attachment](#create-an-attachment)
    - [Retrieve an Attachment](#retrieve-an-attachment)
    - [Remove an Attachment](#remove-an-attachment)
    - [Create Record Signed Copy](#create-record-signed-copy)
    - [Retrieve Record Signed Copy](#retrieve-record-signed-copy)
    - [Remove Record Signed Copy](#remove-record-signed-copy)
  - [Smart Import](#smart-import)
    - [Create a Smart Import Record](#create-a-smart-import-record)
    - [Upload to Existing Import](#upload-to-existing-import)
    - [Retrieve Predictions](#retrieve-predictions)
- [Signature Operations](#signature-operations)
  - [Retrieve Sign Status](#retrieve-sign-status)
  - [Send Signature Request](#send-signature-request)
  - [Cancel Signature Request](#cancel-signature-request)
  - [List All Workflow Signers](#list-all-workflow-signers)
  - [Update a Signer](#update-a-signer)
  - [Delete a Signer](#delete-a-signer)
  - [Remind a Signer](#remind-a-signer)
  - [Create Recipient URL](#create-recipient-url)
  - [Create Embeddable Recipient URL](#create-embeddable-recipient-url)
- [Entity Operations](#entity-operations)
  - [List Relationship Types](#list-relationship-types)
  - [Get Relationship Type](#get-relationship-type)
  - [Create an Entity](#create-an-entity)
  - [Retrieve an Entity](#retrieve-an-entity)
  - [List All Entities](#list-all-entities)
  - [Update an Entity](#update-an-entity)
  - [Delete an Entity](#delete-an-entity)
- [Webhook Operations](#webhook-operations)
  - [Create Webhook](#create-webhook)
  - [Retrieve a Webhook](#retrieve-a-webhook)
  - [Update a Webhook](#update-a-webhook)
  - [Delete a Webhook](#delete-a-webhook)
- [Data Export Operations](#data-export-operations)
  - [Create a Data Export Job](#create-a-data-export-job)
  - [Retrieve Data Export Job Status](#retrieve-data-export-job-status)
  - [Download Data Export File](#download-data-export-file)
- [User & Group Operations (SCIM)](#user-group-operations-scim)
  - [Users](#users)
  - [Groups](#groups)

---

## 🧩 General Concepts

### Dynamic Dropdowns

Many input fields in this connector populate their options dynamically from your Ironclad instance. For example:

- **Template** dropdowns call `List all Workflow Schemas` to show your available workflow templates.
- **Record Type** dropdowns call `Retrieve Record Schemas` to show your configured record types.
- **Property Name** dropdowns call `Retrieve Record Schemas` to list available property system names.
- **Relationship Type** dropdowns call `List Relationship Types` for entity operations.

These dropdowns appear automatically in the Power Automate designer. When using dynamic content from a prior step (e.g. a workflow ID from a trigger), the schema may not render in the designer — but the action still works at runtime.

### Impersonation Headers

Most operations support two optional headers for acting on behalf of another user:

| Header | Description |
|--------|-------------|
| `x-as-user-email` | The email of the user to impersonate |
| `x-as-user-id` | The user ID to impersonate |

One of these is **required** when the connector's OAuth token was produced via the Client Credentials grant. For Authorization Code tokens (the default setup), these are optional and can be left empty.

### Complex Data Types

Ironclad uses several complex data types for workflow attributes and record properties. These are handled differently when **reading** vs **writing**.

#### Reading (output from Retrieve/List actions)

The connector transforms Ironclad's raw responses into user-friendly formats:

| Type | Output Format | Example |
|------|--------------|---------|
| **String** | Plain string | `"Acme Corp"` |
| **Number** | Number | `42` |
| **Boolean** | Boolean | `true` |
| **Date** | ISO 8601 string | `"2024-01-31T00:00:00Z"` |
| **Monetary** | Object with `amount` and `currency` | `{"amount": "1598.12", "currency": "EUR"}` |
| **Duration** | Object with breakdown | `{"isoDuration": "P1Y2M", "years": 1, "months": 2, "weeks": 0, "days": 0}` |
| **Address** | Object with parts | `{"lines": [...], "locality": "...", "region": "...", "postcode": "...", "country": "..."}` |
| **Multi-select** | Array of strings | `["Option A", "Option B"]` |
| **Dropdown** | String | `"Selected Option"` |

#### Writing (input for Create/Update actions)

> ⚠️ **This is the most common source of errors.** When updating or creating records/workflows, you must use Ironclad's raw API format — not the friendly read format.

| Type | ❌ Don't Use (Read Format) | ✅ Use (Write Format) |
|------|---------------------------|----------------------|
| **Monetary** | `{"amount": "1598.12", "currency": "EUR"}` as a formatted string | `{"currency": "EUR", "amount": "1598.12"}` as a **JSON object** |
| **Date** | `"31st January 2024"` | `"2024-01-31T00:00:00Z"` |
| **Duration** | `{"years": 1, "months": 2}` | `"P1Y2M"` (ISO 8601) |
| **Address** | Structured object | Newline-separated string: `"123 Main St\nSuite 4\nNew York, NY 10001"` |
| **Multi-select** | Array | JSON array as a string: `["Option A", "Option B"]` |

##### How to Set Complex Types in Power Automate

When updating a **monetary value** on a workflow or record, you need to pass the JSON object as the value. In Power Automate, use an expression like:

```
json(concat('{"currency":"EUR","amount":"', variables('Amount'), '"}'))
```

Or use a Compose action to build the object, then reference it as the value.

For **arrays** (multi-select fields), pass a JSON array string:

```
json(concat('["', variables('Option1'), '","', variables('Option2'), '"]'))
```

> 💡 The connector automatically attempts to parse string values that look like JSON objects, arrays, numbers, or booleans into their proper types. However, it's best practice to construct them explicitly.

### Pagination

List operations return paginated results with these common fields:

| Field | Description | Default |
|-------|-------------|---------|
| `page` | Page number (0-indexed for most, 1-indexed for some) | `0` |
| `pageSize` | Number of results per page | `20` |
| `count` | Total number of results available | — |
| `list` | Array of result items | — |

To retrieve all results, loop while `page * pageSize < count`.

---

## 📋 Workflow Operations

### Schemas

#### List all Workflow Schemas

> `GET` · `ListWorkflowSchemas`

Returns a list of workflow templates configured in your Ironclad instance.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `form` | string | No | Set to `launch` to get launch-form schemas |
| `includeAll` | boolean | No | When `true`, prepends an "All Templates" entry |

**Output:** Array of `{id, name}` objects.

This action is primarily used internally by other actions' dynamic dropdowns (e.g. the Template picker in workflow creation). You can also call it directly to enumerate available templates.

---

#### Retrieve a Workflow Schema

> `GET` · `RetrieveWorkflowSchema`

Returns the field definitions for a specific workflow template's launch form.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Schema` | string | Yes | The template ID (from the Template dropdown) |
| `form` | string | No | Set to `launch` to get launch-form fields |

**Output includes:**

| Field | Description |
|-------|-------------|
| `schemaAsArray` | Array of `{systemName, displayName, type, required}` for each field |
| `documentSchemaAsArray` | Array of document attachment fields |
| `launchSchema` | The full schema object with field definitions |
| `formattedSchema` | OpenAPI-formatted schema for dynamic rendering |

> 💡 This is the action that powers the dynamic input form when creating workflows. You can also call it to discover what fields a template expects before launching a workflow programmatically.

---

#### Retrieve Formatted Workflow Schema

> `GET` · `RetrieveFormattedWorkflowSchema`

Returns a formatted OpenAPI schema for workflow attributes, used internally to render dynamic output fields in List All Workflows V2.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `templateId` | string | No | Filter to a specific template's fields |

> 🔧 This is an internal schema operation. You generally don't need to call it directly — it's invoked behind the scenes by the List All Workflows V2 action.

---

### Lifecycle

#### Create a Workflow Synchronously

> `POST` · `CreateWorkflow`

Launches a new workflow and waits for Ironclad to fully process it before returning.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `form` | string | Yes | Always `launch` |
| `template` | string | Yes | The template ID (dropdown) |
| `attributes` | dynamic | Yes | The launch form fields — rendered dynamically based on the selected template |

The `attributes` input is a **dynamic schema** that changes based on the selected template. The Power Automate designer renders the template's launch form fields (text fields, dropdowns, dates, etc.) as native input controls.

**How it works internally:** The connector converts the JSON input into a multipart form-data request. If any attributes contain file arrays (document fields), those are extracted and sent as separate file parts in the multipart payload.

**Output:** The fully hydrated workflow object (same as `Retrieve a Workflow`).

> ⚠️ **Timeout Risk:** This action has a Power Platform-imposed timeout. For connectors installed **before mid-2025**, the timeout is relatively short and complex workflows frequently fail. Use the [async pattern](#create-a-workflow-asynchronously) instead. Connectors installed **after mid-2025** benefit from Microsoft's increased timeout and can use this action more reliably.

**Error handling:** If the launch fails due to a missing approver assignment, the connector transforms the error into a clearer message: *"Missing assignment for approval role: \<roleName\>. Please add it to the Launch Approvals."*

---

#### Create a Workflow Asynchronously

> `POST` · `CreateWorkflowAsync`

Launches a new workflow without waiting for completion. Returns immediately with a job ID you can poll.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `form` | string | Yes | Always `launch` |
| `template` | string | Yes | The template ID (dropdown) |
| `attributes` | dynamic | Yes | The launch form fields (same dynamic schema as sync) |

**Output:**

| Field | Description |
|-------|-------------|
| `asyncJobId` | The job ID to poll with `Retrieve Async Workflow Status` |
| `asyncJobStatusUrl` | The URL for status polling |

**Handling Launch Approvals:** If your template requires approvers to be assigned at launch, use the `launchApprovals` array inside `attributes`:

```json
{
  "template": "your-template-id",
  "attributes": {
    "fieldName": "value",
    "launchApprovals": [
      {
        "roleName": "approverRoleSystemName",
        "assignee": "user@company.com"
      }
    ]
  }
}
```

The `roleName` is the system name of the approval role (visible in the workflow schema), and `assignee` is the user's email address.

> ⚠️ **Complex/nested attributes:** The connector unflattens slash-separated field names into nested objects. For example, `{"address/city": "London"}` becomes `{"address": {"city": "London"}}`. This happens automatically — you don't need to handle nesting yourself in the designer.

##### Recommended Async Pattern

```
1. Create a Workflow Asynchronously
2. Delay (10-15 seconds)
3. Retrieve Async Workflow Status → check if complete
4. Retrieve a Workflow → get the full workflow data
5. Create a Workflow Document → attach any documents
```

---

#### Create a Workflow (Raw Body)

> `POST` · `CreateWorkflowRaw`

Launches a workflow with a raw, unstructured JSON body. No template-based dynamic schema is applied.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `form` | string | Yes | Always `launch` |
| `body` | object | Yes | Free-form JSON matching the Ironclad API's expected workflow creation payload |

**Output:** Generic object.

> 💡 Use this when you need full control over the request body or when the dynamic schema doesn't suit your needs (e.g. building the payload dynamically from variables). You must match Ironclad's raw API format exactly — consult the [Ironclad API docs](https://developer.ironcladapp.com/reference/create-a-workflow).

---

#### Retrieve Async Workflow Status

> `GET` · `RetrieveAsyncJobStatus`

Checks the status of an asynchronously created workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `AsyncJob` | string | Yes | The async job ID returned by `Create a Workflow Asynchronously` |

**Output:** Status object including completion state and the resulting workflow ID when complete.

---

#### Retrieve a Workflow

> `GET` · `RetrieveWorkflow`

Returns the full details of a specific workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID or Ironclad ID |

**Output includes:**

| Field | Description |
|-------|-------------|
| `id` | Internal workflow ID |
| `ironcladId` | Human-readable Ironclad ID (e.g. `IC-12345`) |
| `title` | Workflow title |
| `step` | Current step (`launch`, `review`, `sign`, `complete`, `cancelled`) |
| `status` | Workflow status |
| `creator` | Creator information |
| `formattedAttributes` | All workflow attributes formatted with user-friendly types |
| `formattedSchema` | The schema definition for attributes (used for dynamic rendering) |
| `formattedDocuments` | Document attachment metadata |

The `formattedAttributes` object contains every attribute with values transformed to the [friendly read format](#reading-output-from-retrievelist-actions). Monetary values appear as objects with `amount`/`currency`, durations are expanded with `years`/`months`/`weeks`/`days`, etc.

---

#### List all Workflows

> `GET` · `ListAllWorkflows`

Returns a paginated list of all workflows.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | integer | No | Page number (default: 0) |
| `pageSize` | integer | No | Results per page (default: 20) |
| `template` | string | No | Filter by template ID |
| `status` | string | No | Filter by status |

**Output:** Paginated list with `count`, `page`, `pageSize`, and `list` array.

Each workflow in the list includes a `label` field formatted as `"IC-12345: Workflow Title"` for easy identification.

---

#### List All Workflows V2

> `POST` · `ListAllWorkflowsV2`

Query workflows with structured filtering, status multi-select, and formatted attribute output.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | integer | No | Page number |
| `pageSize` | integer | No | Results per page |
| `lastUpdated` | string | No | ISO date — only return workflows updated after this date |
| `template` | string | No | Filter to a single template (dropdown from your templates, or "All Templates") |
| `status` | array | No | Filter by one or more statuses: `active`, `paused`, `completed`, `cancelled` |
| `filters` | array | No | Structured filter conditions (see below) |

**Filter conditions** follow the same format as [List All Records V2 filters](#list-all-records-v2):

```json
{
  "filters": [
    {
      "property": "counterpartyName",
      "operator": "Contains",
      "values": ["Acme"]
    }
  ]
}
```

The `property` dropdown is dynamically populated based on the selected template. Filter operators available:

| Operator | Description |
|----------|-------------|
| `Equals` | Exact match |
| `NotEqual` | Not equal |
| `Contains` | Contains substring |
| `IsEmpty` | Field has no value (no `values` needed) |
| `IsNotEmpty` | Field has a value (no `values` needed) |
| `LessThan` | Less than (dates, numbers) |
| `LessThanOrEqual` | Less than or equal |
| `GreaterThan` | Greater than |
| `GreaterThanOrEqual` | Greater than or equal |

Multiple filters are combined with **AND**. Multiple values within a single filter are combined with **OR**.

**Output:** Each workflow includes `label`, `counterpartyName` (promoted to root), and `formattedAttributes` with values transformed using the [friendly read format](#reading-output-from-retrievelist-actions). The raw `schema` and `attributes` fields are stripped.

> 💡 **Tip:** Selecting a specific template in the Template dropdown enables the output schema to render template-specific columns in the designer. Using "All Templates" returns all workflows but the output schema is generic.

---

#### Update Workflow Metadata

> `PATCH` · `UpdateWorkflowMetadata`

Updates attributes on a workflow that is in the **Review** step.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `configuration` | string | Yes | The template ID (used to populate the attribute dropdown) |
| `comment` | string | No | A comment to add to the workflow's activity feed |
| `updates` | array | Yes | Array of update actions |

Each update in the `updates` array:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `action` | string | Yes | `set` or `remove` |
| `path` | string | Yes | The attribute system name (dropdown populated from template schema) |
| `value` | dynamic | Yes | The new value |

##### ⚠️ Setting Complex Type Values

This is where the connector requires special care. The `value` field accepts a free-form input, and the connector attempts to auto-detect the type:

| If your value looks like... | It becomes... |
|----------------------------|---------------|
| `{"currency":"EUR","amount":"100"}` | JSON object ✅ |
| `["option1","option2"]` | JSON array ✅ |
| `42` | Number ✅ |
| `true` / `false` | Boolean ✅ |
| Anything else | String ✅ |

**To set a monetary value**, type or compose the JSON object:
```json
{"currency": "EUR", "amount": "1598.12"}
```

**To set a multi-select value**, type or compose the JSON array:
```json
["Option A", "Option B"]
```

**To set a value that looks like a number but should stay as a string** (e.g. a reference number), wrap it in escaped quotes:
```
"12345"
```

> 💡 Fields ending in `_string` in the schema always remain as strings, regardless of content.

---

#### Cancel Workflow

> `POST` · `CancelWorkflow`

Cancels an active workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

#### Pause Workflow

> `POST` · `PauseWorkflow`

Pauses an active workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

#### Resume Workflow

> `POST` · `ResumeWorkflow`

Resumes a paused workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

#### Revert to Review Step

> `PATCH` · `RevertToReview`

Reverts a workflow back to the Review step.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

### Approvals

#### List all Workflow Approvals

> `GET` · `ListWorkflowApprovals`

Returns all approval steps and their statuses for a workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

#### Retrieve Approval Requests

> `GET` · `ListApprovalRequests`

Returns all approval requests (pending and completed) for a workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

#### Update Approval on a Workflow

> `PATCH` · `UpdateWorkflowApprovals`

Approves or rejects a specific approval role on a workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `Role` | string | Yes | The approval role to update |
| `body` | object | Yes | The approval action (status, comment) |

> ⚠️ **Dynamic schema limitation:** The approval role dropdown requires the workflow to be explicitly specified. When the workflow ID comes from dynamic content (e.g. a trigger), the dropdown may not populate in the designer — but you can type the role name manually and it works at runtime.

---

### Comments

#### Create a Comment

> `POST` · `CreateComment`

Adds a comment to a workflow's activity feed.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `comment` | string | Yes | The comment text |

---

#### List all Comments

> `GET` · `ListWorkflowComments`

Returns all comments on a workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

#### Retrieve a Comment

> `GET` · `RetrieveComment`

Returns a single comment from a workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `Comment` | string | Yes | The comment ID |

---

### Documents

#### Create a Workflow Document

> `POST` · `CreateWorkflowDocument`

Uploads a document to a specific attribute on a workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `Attribute` | string | Yes | The document attribute system name |
| `attachment` | file (binary) | Yes | The document file content |
| `metadata.filename` | string | No | The filename for the uploaded document |

The connector converts the input into a multipart form-data request with the file content decoded from base64.

> 💡 **Attaching documents after async creation:** After creating a workflow asynchronously, retrieve the workflow to discover the document attribute names, then use this action to attach each document.

---

#### Create a Signed Document

> `POST` · `CreateSignedDocument`

Uploads a signed document to a workflow that is in the **Sign** step.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `attachment` | file | Yes | The signed document file |

---

#### Retrieve a Workflow Document

> `GET` · `RetrieveWorkflowDocument`

Downloads a document from a workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `Key` | string | Yes | The document key/attribute |

**Output:** Binary file content.

---

### Email

#### Retrieve Email Threads

> `GET` · `RetrieveEmailThreads`

Lists all email threads in a workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

#### Retrieve an Email Thread

> `GET` · `RetrieveEmailThread`

Returns a single email thread with attachments.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `Email` | string | Yes | The email thread ID |

**Output enrichment:** The connector extracts the document key from each attachment's download URL and adds it as a `key` field, making it easier to reference attachments in subsequent actions.

---

### Participants & Turn History

#### List all Workflow Participants

> `GET` · `ListWorkflowParticipants`

Returns all participants on a workflow and their roles.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

#### Retrieve Turn History

> `GET` · `ListTurnHistory`

Returns the turn-by-turn history of a workflow showing what happened at each step.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

## 📁 Record Operations

### Record Schemas

#### Retrieve Record Schemas

> `GET` · `RetrieveRecordSchemas`

Returns the schema definitions for contract records, including record types, properties, clauses, and attachments.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `includeRecordTypes` | boolean | No | Include available record types (default: `true`) |
| `includeProperties` | boolean | No | Include property definitions (default: `true`) |
| `includeClauses` | boolean | No | Include clause definitions (default: `true`) |
| `includeAttachments` | boolean | No | Include attachment definitions (default: `true`) |
| `filterableOnly` | boolean | No | Only include properties that support filtering (default: `false`) |

**Output includes:**

| Field | Description |
|-------|-------------|
| `formattedRecordTypes` | Array of `{systemName, displayName}` for record types |
| `formattedProperties` | Array of `{systemName, label, type}` for properties |
| `formattedClauses` | Array of clause definitions |
| `formattedAttachments` | Array of attachment definitions |

> 💡 This is the operation that powers the dropdowns in record creation and filtering. Use the `filterableOnly` flag to get only properties that support comparison operators (excludes `duration`, `address`, `clause`, `document`, `playbookClause`, `reference`, and `workflow` types which only support `IsEmpty`/`IsNotEmpty`/`Contains`).

---

#### Retrieve Formatted Record Schema

> `GET` · `RetrieveFormattedRecordSchema`

Returns a formatted OpenAPI schema used internally by List All Records V2 for dynamic output rendering.

> 🔧 Internal schema operation — typically called automatically by the connector.

---

### Record CRUD

#### Create a Record

> `POST` · `CreateRecord`

Creates a new contract record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | Yes | The record name |
| `type` | string | Yes | The record type (dropdown populated from your record schemas) |
| `propertiesAsArray` | array | Yes | Array of property values (see below) |
| `children` | array | No | Child record IDs |
| `links` | array | No | Linked record IDs |
| `parent` | object | No | Parent record `{recordId: "..."}` |

Each item in `propertiesAsArray`:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `propertySystemName` | string | Yes | The property system name (dropdown) |
| `value` | dynamic | Yes | The property value |

The connector automatically looks up the property type from your record metadata and wraps the value in the format Ironclad expects (`{value, type}`).

**Output:** The created record object with `counterpartyName` promoted to the root level.

> ⚠️ **Property values** must use Ironclad's raw format. See [Complex Data Types — Writing](#writing-input-for-createupdate-actions).

---

#### Retrieve a Record

> `GET` · `RetrieveRecord`

Returns a record with all its properties, attachments, and metadata.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID or Ironclad ID |

**Output includes:**

| Field | Description |
|-------|-------------|
| `id` | Internal record ID |
| `ironcladId` | Human-readable Ironclad ID |
| `name` | Record name |
| `recordType` | Record type system name |
| `formattedProperties` | Dynamic schema — properties rendered with friendly types based on the record's schema |
| `propertiesAsArray` | Array of `{property, value, type, title, description}` |
| `propertiesByTitle` | Properties keyed by display name |
| `attachmentsAsArray` | Attachments as an array for easier iteration |
| `formattedSchema` | The schema used for dynamic rendering |

The `formattedProperties` field uses a **dynamic schema** — its structure adapts based on the actual record's properties. Monetary values are objects, durations are expanded, etc.

---

#### Update Record Metadata

> `PATCH` · `UpdateRecordMetadata`

Updates specific properties on a record without replacing the entire record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID (dropdown) |
| `name` | string | No | New record name |
| `type` | string | No | New record type |
| `addProperties` | array | No | Properties to add/update (see below) |
| `removeProperties` | array | No | Property system names to remove |
| `addChildren` | array | No | Child record IDs to add |
| `addLinks` | array | No | Linked record IDs to add |
| `removeChildren` | array | No | Child record IDs to remove |
| `removeLinks` | array | No | Linked record IDs to remove |
| `removeParent` | boolean | No | Remove the parent relationship |
| `setParent` | object | No | Set a new parent: `{recordId: "..."}` |

Each item in `addProperties`:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `propertySystemName` | string | Yes | The property system name (dropdown) |
| `value` | dynamic | Yes | The new value |

> ⚠️ **Same complex type rules apply** as for [Create a Record](#create-a-record). The connector looks up property types from metadata and wraps values automatically.

**Output:** The updated record with `counterpartyName` promoted to root.

---

#### Replace a Record

> `PUT` · `ReplaceRecord`

Replaces an entire record's metadata. This is a full replacement — any properties not included in the request will be removed.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID |
| `name` | string | Yes | Record name |
| `type` | string | Yes | Record type |
| `propertiesAsArray` | array | Yes | Complete set of properties |
| `children` | array | No | Child record IDs |
| `links` | array | No | Linked record IDs |
| `parent` | object | No | Parent record |

> ⚠️ **Destructive operation** — omitted properties are removed. Use `Update Record Metadata` for partial updates.

---

#### Delete a Record

> `DELETE` · `DeleteRecord`

Permanently deletes a record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID |

---

### Querying Records

#### List All Records V2 ⭐

> `POST` · `ListAllRecordsV2`

The recommended way to query records. Supports structured filtering, sorting, and type filtering with a guided expression builder.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | integer | No | Page number (default: 0) |
| `pageSize` | integer | No | Results per page (default: 20) |
| `lastUpdated` | string | No | ISO date — only return records updated after this date |
| `sortField` | string | No | Sort by: `lastUpdated` (default), `agreementDate`, or `name` |
| `sortDirection` | string | No | `DESC` (default) or `ASC` |
| `types` | array | No | Filter to specific record types (dropdown from your schemas) |
| `filters` | array | No | Structured filter conditions |

**Filter conditions:**

```json
{
  "filters": [
    {
      "property": "counterpartyName",
      "operator": "Contains",
      "values": ["Acme"]
    },
    {
      "property": "agreementDate",
      "operator": "GreaterThan",
      "values": ["2024-01-01"]
    }
  ]
}
```

Each filter:

| Field | Type | Description |
|-------|------|-------------|
| `property` | string | Property system name (dropdown) |
| `operator` | string | Comparison operator |
| `values` | array | One or more values to compare |

**Available operators:**

| Operator | Description | Requires Values? |
|----------|-------------|-----------------|
| `Equals` | Exact match | Yes |
| `NotEqual` | Not equal | Yes |
| `Contains` | Substring match | Yes |
| `IsEmpty` | No value set | No |
| `IsNotEmpty` | Has a value | No |
| `LessThan` | Less than | Yes |
| `LessThanOrEqual` | Less than or equal | Yes |
| `GreaterThan` | Greater than | Yes |
| `GreaterThanOrEqual` | Greater than or equal | Yes |

**Filter behaviour:**
- Multiple filters are combined with **AND**
- Multiple values within one filter are combined with **OR**
- Date values (ISO format `yyyy-MM-dd`) are auto-converted to Ironclad's `Date(year, month, day)` format
- Boolean values (`true`/`false`, `yes`/`no`) are normalised automatically
- Some property types (`duration`, `address`, `clause`, `document`, `playbookClause`, `reference`, `workflow`) only support `IsEmpty`, `IsNotEmpty`, and `Contains`

**How it works internally:** The connector translates the POST body into a GET request to `/public/api/v1/records` with query parameters. The `filters` array is compiled into Ironclad's formula syntax (e.g. `And(Contains([counterpartyName], 'Acme'), GreaterThan([agreementDate], Date(2024, 1, 1)))`).

**Output enrichment:** Each record in the response includes:
- `label` — formatted as `"IC-12345: Record Name"`
- `attachmentArray` — attachments as an array
- `recordProperties`, `recordClauses`, `recordAttachments` — properties organised by category

---

#### List All Records (Deprecated)

> ~~`GET` · `ListAllRecords`~~

The original record listing endpoint. **Deprecated — use [List All Records V2](#list-all-records-v2) instead.**

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | integer | No | Page number |
| `pageSize` | integer | No | Results per page |
| `recordProperties` | string | No | Comma-separated property names to include in the response |

Each record includes `label`, `counterpartyName`, and `formattedAttachments`.

---

#### Retrieve XLSX Export

> `GET` · `ExportRecords`

Exports records as an XLSX (Excel) file.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `properties` | string | No | Comma-separated list of properties, clauses, and attachments to include |
| `types` | string | No | Comma-separated list of record types to export (e.g. `mutualNDA,NDA`) |

**Output:** Binary XLSX file.

---

### Attachments & Signed Copies

#### Create an Attachment

> `POST` · `CreateAttachment`

Uploads a file attachment to a specific attachment slot on a record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID |
| `Key` | string | Yes | The attachment key/slot name |
| `attachment` | file (binary) | Yes | The file content |
| `metadata.filename` | string | No | The filename (defaults to `document.pdf`) |

The connector converts the JSON input into a multipart form-data request, decoding the base64 file content.

---

#### Retrieve an Attachment

> `GET` · `RetrieveAttachment`

Downloads an attachment from a record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID |
| `Key` | string | Yes | The attachment key |

**Output:** Binary file content.

---

#### Remove an Attachment

> `DELETE` · `RemoveAttachment`

Removes an attachment from a record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID |
| `Key` | string | Yes | The attachment key |

---

#### Create Record Signed Copy

> `POST` · `CreateSignedCopyAttachment`

Uploads a signed copy document to a record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID |
| `attachment` | file (binary) | Yes | The signed document file |
| `metadata.filename` | string | No | The filename |

---

#### Retrieve Record Signed Copy

> `GET` · `RetrieveSignedCopyDocument`

Downloads the signed copy from a record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID |

**Output:** Binary file content.

---

#### Remove Record Signed Copy

> `DELETE` · `RemoveSignedCopyDocument`

Removes the signed copy from a record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Record` | string | Yes | The record ID |

---

### Smart Import

#### Create a Smart Import Record

> `POST` · `CreateSmartImportRecord`

Uploads a document to create a record using Ironclad's Smart Import (AI-powered field extraction).

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `attachment` | file | Yes | The document to import |

---

#### Upload to Existing Import

> `POST` · `ExistingSmartImportRecord`

Adds another file to an existing smart import job.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Import` | string | Yes | The import job ID |
| `attachment` | file | Yes | The additional document |

---

#### Retrieve Predictions

> `GET` · `RetrievePredictions`

Gets the status and results of Smart Import predictions.

---

## ✍️ Signature Operations

> 🧪 Operations marked with 🧪 are untested — the publisher does not have access to Ironclad Signature. They are implemented based on API documentation.

All signature operations require the workflow to be in the **Sign** step.

### Retrieve Sign Status

> `GET` · `RetrieveSignStatus`

Returns the current signature status of a workflow.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

### Send Signature Request

> `POST` · `SendSignatureRequest`

Sends the signature packet out for signatures.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

> ⚠️ All signers must have signature and initials tags placed in the document before sending.

---

### Cancel Signature Request

> `POST` · `CancelSignatureRequest`

Cancels a signature request that is currently out for signature.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

### List All Workflow Signers

> `GET` · `ListWorkflowSignatures`

Returns all signers on a workflow and their signature status.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

### Update a Signer

> `PATCH` · `UpdateWorkflowSigner`

Updates a signer's details (email, name).

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `SignerRole` | string | Yes | The signer role identifier |
| `body` | object | Yes | Updated signer details |

> ⚠️ **Adobe Sign:** Updating a signer cancels the current signature request. You'll need to resend.

---

### Delete a Signer

> `DELETE` · `DeleteWorkflowSigner`

Removes a signer from the signature packet.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `SignerRole` | string | Yes | The signer role to remove |

> ⚠️ **This cancels the current signature packet.** You'll need to resend the signature request after removing a signer.

---

### Remind a Signer

> `POST` · `RemindWorkflowSigner`

Sends a reminder email to a specific signer.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |
| `SignerRole` | string | Yes | The signer role to remind |

> ⚠️ **Adobe Sign:** Reminding one signer sends reminders to ALL signers.

---

### Create Recipient URL

> 🧪 `POST` · `CreateSignatureRecipientUrl`

Creates a URL for a signer to access their signature page. *Ironclad Signature only.*

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

### Create Embeddable Recipient URL

> 🧪 `POST` · `CreateEmbeddedRecipientUrl`

Creates an embeddable URL for iframe integration. *Ironclad Signature only.*

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Workflow` | string | Yes | The workflow ID |

---

## 🔗 Entity Operations

Entities in Ironclad represent organisations, people, or other reference data that can be linked to workflows and records through relationship types.

### List Relationship Types

> `GET` · `ListEntityRelationshipTypes`

Returns all entity relationship types configured in your Ironclad instance.

**Output:** Array of relationship types, each with their property definitions, formatted as `{systemName, displayName, properties}`.

---

### Get Relationship Type

> `GET` · `GetEntityRelationshipType`

Returns details of a specific relationship type, including its property schema.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Relationship` | string | Yes | The relationship type system name |

**Output includes:**

| Field | Description |
|-------|-------------|
| `formattedSchema` | OpenAPI-formatted property schema for this relationship type |
| `propertiesAsArray` | Array of property definitions |

> 🔧 Internally, the connector rewrites this from a single-item lookup to a list request and filters to the requested item, since Ironclad's API doesn't have a direct single-item endpoint.

---

### Create an Entity

> `POST` · `CreateEntity`

Creates a new entity.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | Yes | Entity name |
| `relationshipTypeKey` | string | Yes | The relationship type (dropdown) |
| `status` | string | No | `ACTIVE` (default) or `INACTIVE` |
| `properties` | dynamic | No | Property values — schema rendered dynamically based on selected relationship type |

The `properties` input uses a **dynamic schema** based on the selected relationship type. The connector:
- Normalises type names (e.g. `monetaryamount` → `monetary_amount`)
- Converts duration values to ISO 8601 format
- Wraps `relationshipTypeKey` in an array as required by Ironclad's API

**Output:** The created entity with `{id, name, ironcladId, status, properties}`.

---

### Retrieve an Entity

> `GET` · `RetrieveEntity`

Returns a specific entity with all its properties and relationship details.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Entity` | string | Yes | The entity ID |

**Output enrichment:** The connector adds:
- `propertiesAsArray` — properties as an iterable array
- `relationshipTypes` — the relationship type details
- `label` — formatted entity label

---

### List All Entities

> `GET` · `ListAllEntities`

Returns a paginated list of all entities.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `page` | integer | No | Page number |
| `pageSize` | integer | No | Results per page |

Each entity in the response is enriched the same way as `Retrieve an Entity`.

---

### Update an Entity

> `PATCH` · `UpdateEntity`

Updates an entity's properties.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Entity` | string | Yes | The entity ID |
| `name` | string | Yes | Entity name |
| `relationshipTypeKey` | string | Yes | Relationship type |
| `removeProperties` | array | No | Property keys to remove |
| `addProperties` | array | No | Properties to add/update: `[{key, value}]` |

The dynamic property schema is based on the selected relationship type.

---

### Delete an Entity

> `DELETE` · `DeleteEntity`

Deletes an entity.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Entity` | string | Yes | The entity ID |

---

## 🔔 Webhook Operations

Webhooks allow you to receive real-time notifications when events occur in Ironclad. The connector registers as a **trigger** in Power Automate, meaning you can use "When a webhook event occurs" as a flow trigger.

### Create Webhook

> `POST` · `CreateWebhook`

Registers a new webhook endpoint. In Power Automate, this is used as a **trigger** — the `targetURL` is automatically set to Power Automate's callback URL.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `events` | array | Yes | The events to subscribe to |

**Available events:**

| Event | Description |
|-------|-------------|
| `workflow_launched` | A workflow was created/launched |
| `workflow_updated` | A workflow was updated |
| `workflow_completed` | A workflow reached completion |
| `workflow_cancelled` | A workflow was cancelled |
| `workflow_paused` | A workflow was paused |
| `workflow_resumed` | A workflow was resumed |
| `workflow_approval_status_changed` | An approval status changed |
| `workflow_attribute_updated` | A workflow attribute was modified |
| `workflow_comment_added` | A comment was added |
| `workflow_comment_removed` | A comment was removed |
| `workflow_comment_updated` | A comment was updated |
| `workflow_comment_reaction_added` | A reaction was added to a comment |
| `workflow_comment_reaction_removed` | A reaction was removed from a comment |
| `workflow_counterparty_invite_sent` | A counterparty invite was sent |
| `workflow_counterparty_invite_revoked` | A counterparty invite was revoked |
| `workflow_documents_added` | Documents were added |
| `workflow_documents_removed` | Documents were removed |
| `workflow_documents_updated` | Documents were updated |
| `workflow_documents_renamed` | Documents were renamed |
| `workflow_document_edited` | A document was edited |
| `workflow_signature_packet_sent` | A signature packet was sent |
| `workflow_signature_packet_uploaded` | A signed packet was uploaded |
| `workflow_signature_packet_cancelled` | A signature request was cancelled |

**Webhook payload** (received by Power Automate trigger):

| Field | Description |
|-------|-------------|
| `webhookID` | The webhook registration ID |
| `companyID` | Your Ironclad company/instance ID |
| `timestamp` | When the event occurred |
| `payload` | The event data (contains workflow details) |

---

### Retrieve a Webhook

> `GET` · `RetrieveWebhook`

Returns the configuration of a registered webhook.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Webhook` | string | Yes | The webhook ID |

---

### Update a Webhook

> `PATCH` · `UpdateWebhook`

Modifies a webhook's configuration (events, target URL).

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Webhook` | string | Yes | The webhook ID |

---

### Delete a Webhook

> `DELETE` · `DeleteWebhook`

Removes a registered webhook. In Power Automate, this is called automatically when a webhook trigger flow is deleted.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Webhook` | string | Yes | The webhook ID |

---

## 📤 Data Export Operations

> 💰 These operations require the **Security & Data Pro** add-on in Ironclad.

### Create a Data Export Job

> `POST` · `CreateDataExport`

Submits a request to generate a data export file.

---

### Retrieve Data Export Job Status

> `GET` · `RetrieveDataExportStatus`

Checks the status of a data export job.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `ExportJob` | string | Yes | The export job ID |

---

### Download Data Export File

> `GET` · `DownloadDataExport`

Downloads the completed data export file.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `ExportJob` | string | Yes | The export job ID |

**Output:** Binary file content.

---

## 👥 User & Group Operations (SCIM)

These operations use the SCIM 2.0 protocol for user and group management.

### Users

#### Create a User · `POST` · `CreateUser`

Provisions a new user in Ironclad.

#### List all Users · `GET` · `ListUsers`

Lists all provisioned users. The connector enriches each user with a `displayName` and `combinedLabel` field for easier identification in dropdowns and flows.

#### Retrieve a User · `GET` · `RetrieveUser`

Retrieves a specific user by ID.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `User` | string | Yes | The user ID |

#### Replace a User · `PUT` · `ReplaceUser`

Replaces a user's full record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `User` | string | Yes | The user ID |

#### Update a User · `PATCH` · `UpdateUser`

Partially updates a user's attributes using SCIM PatchOp.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `User` | string | Yes | The user ID |

> 🔧 The connector automatically injects the required SCIM PatchOp schema (`urn:ietf:params:scim:api:messages:2.0:PatchOp`) into the request body if it's missing.

#### Delete a User · `DELETE` · `DeleteUser`

Removes a user.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `User` | string | Yes | The user ID |

---

### Groups

#### Create a Group · `POST` · `CreateGroup`

Provisions a new group.

#### List all Groups · `GET` · `ListGroups`

Lists all provisioned groups.

#### Retrieve a Group · `GET` · `RetrieveGroup`

Retrieves a specific group by ID.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Group` | string | Yes | The group ID |

#### Replace a Group · `PUT` · `ReplaceGroup`

Replaces a group's full record.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Group` | string | Yes | The group ID |

#### Update a Group · `PATCH` · `UpdateGroup`

Partially updates a group using SCIM PatchOp.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Group` | string | Yes | The group ID |

> 🔧 Same automatic SCIM PatchOp schema injection as `Update a User`.

#### Delete a Group · `DELETE` · `DeleteGroup`

Removes a group.

| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `Group` | string | Yes | The group ID |

---

<div align="center">

📖 For setup and troubleshooting, see the main [README](README.md).  
📚 For the raw Ironclad API documentation, visit the [Ironclad Developer Center](https://developer.ironcladapp.com/docs/getting-started).

</div>

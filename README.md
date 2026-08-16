# ⚖️ Ironclad CLM Custom Connector

A powerful custom connector for [Ironclad CLM](https://ironcladapp.com/), enabling seamless integration with Microsoft Power Platform (Power Automate, Power Apps, and Copilot Studio).

> 🔧 This connector uses a custom C# script to transform API traffic, making the non-OpenAPI compatible Ironclad API work seamlessly with OpenAPI standards.

---

## 📑 Table of Contents

- [Publisher](#-publisher)
- [Prerequisites](#-prerequisites)
- [Supported Operations](#-supported-operations)
- [📖 Action Reference Guide](REFERENCE.md) — detailed inputs, outputs, and usage tips for every operation
- [Getting Started](#-getting-started)
- [Known Issues and Limitations](#️-known-issues-and-limitations)
- [Common Issues & Troubleshooting](#-common-issues--troubleshooting)
- [FAQ](#-frequently-asked-questions)

---

## 👤 Publisher

**Independent Publisher**
Maximilian Henkensiefken (Amadeus IT Group, S.A.) in collaboration with Ironclad

---

## ✅ Prerequisites

<table style="width:100%">
<thead>
<tr><th>Requirement</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td>🔐 Ironclad Account</td><td>An Ironclad CLM account with APIs enabled (additional charges may apply)</td></tr>
<tr><td>🔑 API Credentials</td><td>Client ID and Secret from a registered application in Ironclad Admin panel</td></tr>
<tr><td>💼 Power Platform License</td><td>Licensed access to Power Automate, Power Apps, or Copilot Studio</td></tr>
<tr><td>🏗️ Environment Maker Role</td><td>Required in the Power Platform environment you are deploying to</td></tr>
</tbody>
</table>

---

## 🔌 Supported Operations

Almost all Ironclad API operations are available. For complete API documentation, visit the [Ironclad Developer Center](https://developer.ironcladapp.com/docs/getting-started).

> 📖 **See the [Action Reference Guide](REFERENCE.md)** for detailed input/output documentation, usage patterns, and tips for every operation.

### 📋 Workflow Operations

#### Schemas

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#list-all-workflow-schemas">List all Workflow Schemas</a></td><td>Returns a list of workflow schemas</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-a-workflow-schema">Retrieve a Workflow Schema</a></td><td>Returns the fields used in the workflow's launch form</td></tr>
</tbody>
</table>

#### Approvals

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#list-all-workflow-approvals">List all Workflow Approvals</a></td><td>Returns a list of approvals for the workflow</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-approval-requests">Retrieve Approval Requests</a></td><td>Returns a list of workflow approval requests</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#update-approval-on-a-workflow">Update Approval on a Workflow</a></td><td>Updates an approval to the specified status</td></tr>
</tbody>
</table>

#### Comments

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-a-comment">Create a Comment</a></td><td>Creates a comment in the workflow's activity feed</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#list-all-comments">List all Comments</a></td><td>Return a list of comments on a workflow</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-a-comment">Retrieve a Comment</a></td><td>Return a single comment for a specified workflow</td></tr>
</tbody>
</table>

#### Documents

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-a-signed-document">Create a Signed Document</a></td><td>Upload a signed document to a workflow in sign step</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-a-workflow-document">Create a Workflow Document</a></td><td>Create a document in the specified workflow attribute</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-a-workflow-document">Retrieve a Workflow Document</a></td><td>Download a document associated with a specific workflow</td></tr>
</tbody>
</table>

#### Email

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-an-email-thread">Retrieve an Email Thread</a></td><td>List a single email thread for a specified workflow</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-email-threads">Retrieve Email Threads</a></td><td>List all email threads in the specified workflow</td></tr>
</tbody>
</table>

#### Participants & Turn History

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#list-all-workflow-participants">List all Workflow Participants</a></td><td>Returns a list of workflow participants</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-turn-history">Retrieve Turn History</a></td><td>An array of objects for each turn on a workflow</td></tr>
</tbody>
</table>

#### Workflow Lifecycle

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#cancel-workflow">Cancel Workflow</a></td><td>Cancel a workflow</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-a-workflow-asynchronously">Create a Workflow Asynchronously</a></td><td>Launch a new workflow asynchronously</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-a-workflow-raw-body">Create a Workflow (Raw Body)</a></td><td>Launch a workflow with a raw JSON request body (no schema transformation)</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-a-workflow-synchronously">Create a Workflow Synchronously</a></td><td>Launch a new workflow synchronously</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#list-all-workflows">List all Workflows</a></td><td>List all workflows in your Ironclad account</td></tr>
<tr><td><code>POST</code></td><td><strong><a href="REFERENCE.md#list-all-workflows-v2">List All Workflows V2</a></strong> ⭐</td><td>Query workflows with structured filtering, status multi-select, and formatted output</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#pause-workflow">Pause Workflow</a></td><td>Pause a workflow</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-a-workflow">Retrieve a Workflow</a></td><td>View the data associated with a specific workflow</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-async-workflow-status">Retrieve Async Workflow Status</a></td><td>Check the status of an asynchronously created workflow</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#resume-workflow">Resume Workflow</a></td><td>Resume a workflow</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#revert-to-review-step">Revert to Review Step</a></td><td>Reverts a workflow to the Review step</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#update-workflow-metadata">Update Workflow Metadata</a></td><td>Update the attributes on a workflow in the Review step</td></tr>
</tbody>
</table>

### ✍️ Signature Operations

> 🧪 **Note:** Operations marked with 🧪 are untested (Ironclad Signature not available to publisher).

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#cancel-signature-request">Cancel Signature Request</a></td><td>Cancel a signature request that was out for signature</td></tr>
<tr><td><code>POST</code></td><td>🧪 <a href="REFERENCE.md#create-embeddable-recipient-url">Create Embeddable Recipient URL</a></td><td>Create an embeddable URL for iframe integration <em>(Ironclad Signature only)</em></td></tr>
<tr><td><code>POST</code></td><td>🧪 <a href="REFERENCE.md#create-recipient-url">Create Recipient URL</a></td><td>Create a recipient URL for signature access <em>(Ironclad Signature only)</em></td></tr>
<tr><td><code>DELETE</code></td><td><a href="REFERENCE.md#delete-a-signer">Delete a Signer</a></td><td>Remove a signer from a signature packet ⚠️ <em>Cancels current packet</em></td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#list-all-workflow-signers">List All Workflow Signers</a></td><td>Returns a list of workflow signers and their signature status</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#remind-a-signer">Remind a Signer</a></td><td>Send a reminder to a signer</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-sign-status">Retrieve Sign Status</a></td><td>Returns sign status information for a workflow in the sign step</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#send-signature-request">Send Signature Request</a></td><td>Send a signature packet out for signature</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#update-a-signer">Update a Signer</a></td><td>Update a signer's details (email, name)</td></tr>
</tbody>
</table>

### 📤 Data Export Operations

> 💰 *Requires Security & Data Pro add-on*

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-a-data-export-job">Create a Data Export Job</a></td><td>Submit a request to generate a new data export</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#download-data-export-file">Download Data Export File</a></td><td>Download the completed data export file</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-data-export-job-status">Retrieve Data Export Job Status</a></td><td>Check the status of a data export job</td></tr>
</tbody>
</table>

### 📁 Record Operations

> ℹ️ **List All Records V2** (`POST`) is the recommended way to query records. It supports structured body filtering, multi-select property filters, and guided filter expressions. The older `List All Records` (`GET`) is preserved for backwards compatibility but is discouraged for new flows.

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-an-attachment">Create an Attachment</a></td><td>Create an attachment for a specific record</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-a-record">Create a Record</a></td><td>Create a contract record with specified metadata properties</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-record-signed-copy">Create Record Signed Copy</a></td><td>Create a signed copy for a specific record</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-a-smart-import-record">Create a Smart Import Record</a></td><td>Upload a file to create a record with smart import</td></tr>
<tr><td><code>DELETE</code></td><td><a href="REFERENCE.md#delete-a-record">Delete a Record</a></td><td>Delete an existing record</td></tr>
<tr><td><code>DELETE</code></td><td><a href="REFERENCE.md#remove-an-attachment">Delete Attachment</a></td><td>Remove an attachment from a specific record</td></tr>
<tr><td><del><code>GET</code></del></td><td><del><a href="REFERENCE.md#list-all-records-deprecated">List All Records</a></del></td><td><del>View all records in the company — deprecated, use List All Records V2</del></td></tr>
<tr><td><code>POST</code></td><td><strong><a href="REFERENCE.md#list-all-records-v2">List All Records V2</a></strong> ⭐</td><td>Query records with structured body, multi-select filtering, and guided expressions</td></tr>
<tr><td><code>DELETE</code></td><td><a href="REFERENCE.md#remove-record-signed-copy">Remove Record Signed Copy</a></td><td>Remove the signed copy from a specific record</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-a-record">Retrieve a Record</a></td><td>View a specific record and its associated data</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-an-attachment">Retrieve an Attachment</a></td><td>View an attachment on a specific record</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-formatted-record-schema">Retrieve Formatted Record Schema</a></td><td>Return the formatted record schema for a specific section</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-predictions">Retrieve Predictions</a></td><td>Get status of predictions for smart import records</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-record-schemas">Retrieve Record Schemas</a></td><td>View the schema associated with contract records</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-record-signed-copy">Retrieve Record Signed Copy</a></td><td>View the signed copy of a specific record</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-xlsx-export">Retrieve XLSX Export</a></td><td>Export a records report with filtering options</td></tr>
<tr><td><code>PUT</code></td><td><a href="REFERENCE.md#replace-a-record">Replace a Record</a></td><td>Update an existing record with new metadata</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#update-record-metadata">Update Record Metadata</a></td><td>Update specific fields on a record</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#upload-to-existing-import">Upload to Existing Import</a></td><td>Add a file to an existing smart import</td></tr>
</tbody>
</table>

### 🔔 Webhook Operations

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-webhook">Create Webhook</a></td><td>Register a new webhook endpoint</td></tr>
<tr><td><code>DELETE</code></td><td><a href="REFERENCE.md#delete-a-webhook">Delete a Webhook</a></td><td>Remove a registered webhook</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-a-webhook">Retrieve a Webhook</a></td><td>Get details of a specific webhook</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#update-a-webhook">Update a Webhook</a></td><td>Modify a webhook's configuration</td></tr>
</tbody>
</table>

### 🔗 Entity Operations

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-an-entity">Create an Entity</a></td><td>Create a new entity</td></tr>
<tr><td><code>DELETE</code></td><td><a href="REFERENCE.md#delete-an-entity">Delete an Entity</a></td><td>Delete an entity</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#get-relationship-type">Get Relationship Type</a></td><td>Get details of a specific entity relationship type</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#list-all-entities">List All Entities</a></td><td>List all entities</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#list-relationship-types">List Relationship Types</a></td><td>List all entity relationship types</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-an-entity">Retrieve an Entity</a></td><td>Get details of a specific entity</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#update-an-entity">Update an Entity</a></td><td>Update entity metadata</td></tr>
</tbody>
</table>

### 👥 User & Group Operations (SCIM)

#### Users

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#users">Create a User</a></td><td>Provision a new user</td></tr>
<tr><td><code>DELETE</code></td><td><a href="REFERENCE.md#users">Delete a User</a></td><td>Remove a user</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#users">List all Users</a></td><td>List all provisioned users</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#users">Retrieve a User</a></td><td>Retrieve a specific user</td></tr>
<tr><td><code>PUT</code></td><td><a href="REFERENCE.md#users">Replace a User</a></td><td>Replace a user's full record</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#users">Update a User</a></td><td>Update a user's attributes</td></tr>
</tbody>
</table>

#### Groups

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#groups">Create a Group</a></td><td>Provision a new group</td></tr>
<tr><td><code>DELETE</code></td><td><a href="REFERENCE.md#groups">Delete a Group</a></td><td>Remove a group</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#groups">List all Groups</a></td><td>List all provisioned groups</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#groups">Retrieve a Group</a></td><td>Retrieve a specific group</td></tr>
<tr><td><code>PUT</code></td><td><a href="REFERENCE.md#groups">Replace a Group</a></td><td>Replace a group's full record</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#groups">Update a Group</a></td><td>Update a group's attributes</td></tr>
</tbody>
</table>

### 📌 Obligation Operations

> ⚠️ **Important:** Ironclad does not expose obligation property schemas via the API. When creating or updating obligations, property keys and their expected types must be entered manually based on your Ironclad configuration in the Data Manager.

> 💡 **Note:** Like Conversational Search, Obligations (`public.obligations.*`) may not be enabled on all Ironclad instances/plans. If you receive a scope error when connecting, use the **No Obligations** variant of your environment's connection type to exclude these scopes.

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#create-an-obligation">Create an Obligation</a></td><td>Create a new obligation on a record</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#retrieve-an-obligation">Retrieve an Obligation</a></td><td>View a specific obligation and its associated data</td></tr>
<tr><td><code>PATCH</code></td><td><a href="REFERENCE.md#update-an-obligation">Update an Obligation</a></td><td>Update an existing obligation</td></tr>
<tr><td><code>DELETE</code></td><td><a href="REFERENCE.md#delete-an-obligation">Delete an Obligation</a></td><td>Delete an obligation (recurring instances return a 409 identifying the parent to delete instead)</td></tr>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#list-all-obligations">List All Obligations</a></td><td>Query obligations with type filtering, property filters, and sort options</td></tr>
<tr><td><code>GET</code></td><td><a href="REFERENCE.md#list-obligation-types">List Obligation Types</a></td><td>Retrieve all obligation types configured in your Ironclad Data Manager</td></tr>
</tbody>
</table>

### 🔍 Search Operations

> 💡 **Note:** The `public.search.conversational` scope may not be available in all Ironclad instances. If you receive a scope error when connecting, contact [Ironclad Support](https://support.ironcladapp.com) to have this feature enabled for your account. Alternatively, use the **No Conversational Search** variant of your environment's connection type to exclude this scope.

<table style="width:100%">
<thead>
<tr><th>Method</th><th>Operation</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td><code>POST</code></td><td><a href="REFERENCE.md#conversational-search">Conversational Search</a></td><td>Search Ironclad contracts and records using natural language queries</td></tr>
</tbody>
</table>

---

## 🚀 Getting Started

> 📚 For official Ironclad setup documentation, see [Setting up the Ironclad Power Automate Connector](https://developer.ironcladapp.com/docs/setting-up-the-ironclad-power-automate-connector).

### Quick-start overview

<table style="width:100%">
<thead>
<tr><th>#</th><th>Step</th><th>Notes</th></tr>
</thead>
<tbody>
<tr><td>1</td><td><strong>Install the connector</strong> in your Power Platform environment</td><td>Installer prints the callback URL automatically</td></tr>
<tr><td>2</td><td><strong>Note the OAuth callback URL</strong> from the installer output</td><td>Manual retrieval only needed if not using the script</td></tr>
<tr><td>3</td><td><strong>Register an Ironclad application</strong> with the callback URL and all required scopes</td><td>In Ironclad Company Settings → API</td></tr>
<tr><td>4</td><td><strong>Create a connection</strong> in Power Automate / Power Apps using your Client ID and Secret</td><td>—</td></tr>
</tbody>
</table>

> ⚠️ Each Ironclad environment (Global, EU1, Demo, Preview) requires a separate application registration. The OAuth callback URL is different for each Power Platform environment — if you deploy to multiple environments, all their callback URLs must be added to the same Ironclad app's Redirect URIs.

---

### 🌍 Supported Environments

<table style="width:100%">
<thead>
<tr><th>Environment</th><th>URL</th><th>Description</th></tr>
</thead>
<tbody>
<tr><td>🌐 Global</td><td><code>ironcladapp.com</code></td><td>Production (majority of customers)</td></tr>
<tr><td>🇪🇺 EU1</td><td><code>eu1.ironcladapp.com</code></td><td>EU Production</td></tr>
<tr><td>🧪 Demo</td><td><code>demo.ironcladapp.com</code></td><td>Sandbox environment</td></tr>
<tr><td>🔮 Preview</td><td><code>preview.ironcladapp.com</code></td><td>Preview features</td></tr>
<tr><td>🔑 Client Credentials</td><td>any of the above</td><td>Machine-to-machine auth (no interactive login)</td></tr>
</tbody>
</table>

Each environment above is offered in **4 scope variants**, selectable when creating the connection:

| Variant | Excludes |
|---|---|
| **Full** | nothing (all scopes) |
| **No Conversational Search** | `public.search.conversational` |
| **No Obligations** | `public.obligations.*` |
| **No Conversational Search and Obligations** | both of the above |

These variants exist because Conversational Search and Obligations are not enabled on every Ironclad instance/plan — connecting with a scope your instance doesn't support causes an "Invalid scope" error. Pick the variant matching what your instance actually has enabled.

---

### Step 1️⃣: Install the Connector

#### Recommended: Run the installer directly (no clone needed)

**Windows — PowerShell (no Python required)**

```powershell
irm https://raw.githubusercontent.com/maxhenkentech/MSPP-IroncladCLM/main/scripts/manage-ironclad-connector.ps1 | iex
```

**macOS / Linux**

```bash
python3 -c 'import pathlib, runpy, tempfile, urllib.request; p = pathlib.Path(tempfile.gettempdir()) / "manage-ironclad-connector.py"; urllib.request.urlretrieve("https://raw.githubusercontent.com/maxhenkentech/MSPP-IroncladCLM/main/scripts/manage-ironclad-connector.py", p); runpy.run_path(str(p), run_name="__main__")'
```

The installer will:

1. Log in to Power Platform (`paconn login`)
2. List your environments and let you select one
3. Ask whether you want to **install** (new) or **update** (existing connector)
4. Download the latest connector payload from GitHub
5. Run `paconn create` or `paconn update`
6. **Print the OAuth callback URL** — copy this for Step 2

> 💡 Both installers use a `dummy` OAuth secret placeholder. The real client ID and client secret are entered later when creating a connection.

#### Alternative: Run from a local clone

**Windows:**

```powershell
.\scripts\manage-ironclad-connector.ps1
```

**macOS / Linux:**

```bash
python3 ./scripts/manage-ironclad-connector.py
```

#### Alternative: Manual deployment with paconn

Navigate to the `connector` directory in the repository:

```powershell
cd connector
paconn login
paconn create --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx --icon icon.png --secret dummy
```

> 💡 The `--secret dummy` parameter is a placeholder. The actual client secret is configured when creating a connection.

##### Retrieving the callback URL manually

> ℹ️ **Only needed if you deployed manually.** The installer script prints the callback URL automatically — skip this if you used the script.

After manual deployment, retrieve the OAuth callback URL from Power Platform:

1. Open [Power Automate](https://make.powerautomate.com) or [Power Apps](https://make.powerapps.com)
2. Navigate to **Custom Connectors**
3. Find and open **Ironclad CLM**
4. Go to the **Security** tab → **Edit**
5. Ensure **OAuth 2.0** is selected
6. Copy the **Redirect URL** at the bottom

![Copy Redirect URL from Power Platform](screenshots/Copy%20Redirect%20URL.png)

> ⚠️ **CRITICAL:** Leave this page **WITHOUT SAVING**. Do not click "Update connector". Simply close the tab after copying the URL.

The redirect URL follows the format: `https://global.consent.azure-apim.net/redirect/<connector-id-without-shared_>`.

##### Updating existing connectors

- If a per-environment settings file already exists, the script uses it automatically.
- If no saved settings file exists, the script queries the selected environment for matching connectors.
- Settings files are stored in:
  - **macOS:** `~/Library/Application Support/IroncladCLM/deployments/<environment-guid>_settings.json`
  - **Windows:** `%APPDATA%\\IroncladCLM\\deployments\\<environment-guid>_settings.json`
  - **Linux:** `$XDG_STATE_HOME/IroncladCLM/deployments/<environment-guid>_settings.json`

---

### Step 2️⃣: Register an Ironclad Application

Using the callback URL from Step 1:

1. Log in to your Ironclad account
2. Navigate to **Company Settings** > **API** tab
3. Click **Create new app** (or edit an existing one)
4. Configure:
   - **Title**: e.g., "Power Platform Connector"
   - **Grant Types**: Select **Authorization Code**
   - **Redirect URIs**: Paste the callback URL from Step 1
   - **Resource Scopes**: Select **all** [required scopes](#-required-scopes)
5. Click **Save** and securely store the **Client ID** and **Client Secret**

![Paste Redirect URL in Ironclad](screenshots/Paste%20Redirect%20URL.png)

📚 For more details, visit the [Ironclad Developer Hub — API Authentication](https://developer.ironcladapp.com/reference/authentication-api).

---

### Step 3️⃣: Create a Connection

1. Return to Power Automate or Power Apps
2. Create a new flow/app using the Ironclad CLM connector
3. When prompted:
   - Select your Ironclad instance (Global, EU1, Demo, or Preview)
   - Enter your **Client ID**
   - Enter your **Client Secret**
4. Complete the OAuth authorization flow

---

### 🔑 Required Scopes

> ⚠️ **IMPORTANT:** You must add **ALL** scopes listed below to your Ironclad application for the connector to work properly. Missing scopes will cause operations to fail.

<details>
<summary><b>📁 Records</b></summary>

```
public.records.readRecords
public.records.createRecords
public.records.updateRecords
public.records.deleteRecords
public.records.readSchemas
public.records.createAttachments
public.records.readAttachments
public.records.deleteAttachments
public.records.createSmartImportRecords
public.records.readSmartImportRecords
public.records.applyContractActions
```
</details>

<details>
<summary><b>📋 Workflows</b></summary>

```
public.workflows.readWorkflows
public.workflows.createWorkflows
public.workflows.updateWorkflows
public.workflows.cancel
public.workflows.readApprovals
public.workflows.updateApprovals
public.workflows.readSignatures
public.workflows.readParticipants
public.workflows.revertToReview
public.workflows.pauseAndResume
public.workflows.createComments
public.workflows.readComments
public.workflows.createDocuments
public.workflows.readDocuments
public.workflows.readSchemas
public.workflows.readTurnHistory
public.workflows.readEmailCommunications
public.workflows.uploadSignedDocuments
public.workflows.readSignStatus
public.workflows.sendSignatureRequests
public.workflows.cancelSignatureRequests
public.workflows.deleteSigners
public.workflows.updateSigners
public.workflows.remindSigners
public.workflows.createSignatureRecipientUrls
public.workflows.createEmbeddableSignerUrls
```
</details>

<details>
<summary><b>🔔 Webhooks</b></summary>

```
public.webhooks.createWebhooks
public.webhooks.readWebhooks
public.webhooks.updateWebhooks
public.webhooks.deleteWebhooks
```
</details>

<details>
<summary><b>🔗 Entities</b></summary>

```
public.entities.readRelationshipTypes
public.entities.readEntities
public.entities.createEntities
public.entities.updateEntities
public.entities.deleteEntities
```
</details>

<details>
<summary><b>📤 Export</b> (requires Security & Data Pro add-on)</summary>

```
public.export.createReports
public.export.readReports
```
</details>

<details>
<summary><b>👥 SCIM (User/Group Management)</b></summary>

```
scim.users.readUsers
scim.users.createUsers
scim.users.updateUsers
scim.users.deleteUsers
scim.groups.readGroups
scim.groups.createGroups
scim.groups.updateGroups
scim.groups.deleteGroups
scim.schemas.readSchemas
```
</details>

<details>
<summary><b>📌 Obligations</b> (may require enablement — contact Ironclad)</summary>

```
public.obligations.readObligations
public.obligations.createObligations
public.obligations.updateObligations
public.obligations.deleteObligations
public.obligations.readTypes
```

> ⚠️ **This scope may not be available in all Ironclad instances**, similar to Conversational Search. If you see an "invalid scope" error, contact [Ironclad Support](https://support.ironcladapp.com) to request enablement, or use the **No Obligations** variant of your environment's connection type, which excludes these scopes.
</details>

<details>
<summary><b>🔍 Search</b> (may require enablement — contact Ironclad)</summary>

```
public.search.conversational
```

> ⚠️ **This scope may not be available in all Ironclad instances.** If you see an "invalid scope" or similar error during the OAuth connection flow, contact [Ironclad Support](https://support.ironcladapp.com) to request enablement for your account. If your instance does not support Conversational Search, use the **No Conversational Search** variant of your environment's connection type, which excludes this scope.
</details>

---

## ⚠️ Known Issues and Limitations

### 📝 Complex Data Types in Updates

When updating workflow or record metadata, use the raw data format:

<table style="width:100%">
<thead>
<tr><th>Type</th><th>❌ Don't Use</th><th>✅ Use</th></tr>
</thead>
<tbody>
<tr><td>Monetary</td><td>"EUR 1,598.12"</td><td><code>{"currency": "EUR", "amount": "1598.12"}</code></td></tr>
<tr><td>Date</td><td>"31st January 2024"</td><td><code>"2024-01-31T00:00:00Z"</code></td></tr>
<tr><td>Address</td><td>Single line</td><td>Use <code>\n</code> for line breaks</td></tr>
</tbody>
</table>

> This only applies to update operations, not reads.

### ⏱️ Synchronous Workflow Creation

The synchronous workflow creation action has a timeout imposed by Microsoft Power Platform:

- ✅ Works reliably for simple workflows with basic approvers
- ❌ Often fails for complex workflows due to the timeout

> 🆕 **If your connector was first installed after mid-2025**, Microsoft has increased the action timeout and you can now safely use synchronous workflow creation for most cases. However, **updating an older connector installation does not retroactively raise the timeout** — if you updated from a pre-mid-2025 install, the old limit still applies. In that case, use the asynchronous pattern below.

**Recommended Pattern for Complex Workflows (or pre-mid-2025 installs):**
1. Use `Create a Workflow Asynchronously`
2. Add a delay (10+ seconds)
3. Check status with `Retrieve Async Workflow Status`
4. Get details with `Retrieve a Workflow`
5. Add documents with `Create a Workflow Document`

### 🔄 Dynamic Schema Limitations

- Approver lists require explicit workflow specification
- Dynamic workflow IDs prevent schema fetching at runtime
- Affects operations like `Update Approval on a Workflow`
- Power Platform enforces an approximate 8 MB limit on the full response used for dynamic schema resolution, so record schema consumers use an internal schema-only endpoint instead of the larger `Retrieve Record Schemas` payload

### 📌 Obligation Property Keys Are Not Schema-Driven

The Ironclad API does not expose obligation property schemas. When creating or updating obligations, the property keys and expected value types are not available to the connector at runtime:

- Property key names must be entered manually (e.g., `dueDate`, `assignee`, `notes`)
- Value types are not validated by the connector — incorrect types will return an API error
- Available obligation types and their property keys are configured in the **Ironclad Data Manager** under **Obligation Types**

For guidance on your organisation's obligation schema, check the Ironclad Data Manager or contact your Ironclad administrator.

### ⏱️ Filtering Obligations by Contract (`parentId`) Can Lag Right After Creation

`parentId` is not one of Ironclad's documented built-in filterable obligation properties, but it works: filtering `List All Obligations` with `{"property": "parentId", "operator": "Equals", "values": ["<record GUID>"]}` returns obligations belonging to that contract record. Two things to know:

- **Use the record's GUID, not its readable ID.** The obligation list response only ever surfaces `parentReadableId` (e.g. `IC-70`) and `parentRecordName` — never the GUID. Resolve the GUID first via `Equals([readableId],'IC-70')` against the records endpoint (or `List All Records`/`Get a Record` in the connector), then use that GUID as the `parentId` filter value.
- **There can be a brief indexing delay immediately after creating an obligation.** Filtering by `parentId` right after `Create an Obligation` can return 0 results; a retry moments later returns the expected obligation. If you're chaining "create obligation" → "filter by contract" in the same flow, add a short delay (a few seconds) or a retry before trusting a 0-result response.
- Filtering by any other parent-related name (`parentReadableId`, `parentRecordId`, `recordId`) returns a `500 SERVER_ERROR` rather than a clean validation error — only `parentId` (as the GUID) is recognized.

### 🚫 Custom Values Blocked by Enum Restrictions (Fixed in v2.x)

Certain connector fields previously had `enum` constraints that blocked users from entering custom values outside the built-in list:

| Field | Operations Affected | What Was Blocked |
|---|---|---|
| **SCIM Group PATCH — Path** | Patch a SCIM Group | Custom SCIM group attributes beyond `members`, `displayName`, `externalId` |
| **Obligation Type Key** | Create Obligation, Update Obligation | Custom type keys configured in the Ironclad Data Manager |
| **Obligation Filter — Property** | List Obligations | Custom obligation property keys beyond the 6 built-in filterable properties |

The `enum` restrictions have been removed from all three fields. You can now type any value directly or use expression mode to pass dynamic values.

---

### ✍️ Signature Operations

> 🧪 **Note:** The Ironclad Signature-specific features (Recipient URLs, Embedded URLs) have not been tested as the publisher does not have access to Ironclad Signature. These operations are implemented based on API documentation. Please report any issues.

<table style="width:100%">
<thead>
<tr><th>Consideration</th><th>Details</th></tr>
</thead>
<tbody>
<tr><td>Workflow Step</td><td>Must be in <strong>Sign</strong> step</td></tr>
<tr><td>Delete Signer</td><td>⚠️ Cancels current signature packet</td></tr>
<tr><td>Adobe Sign</td><td>Reminding one signer reminds ALL; updating cancels request</td></tr>
<tr><td>Embedded URLs</td><td>Ironclad Signature only (not Adobe Sign/DocuSign)</td></tr>
<tr><td>Tag Requirements</td><td>All signers need signature/initials tags before sending</td></tr>
</tbody>
</table>

### 🌍 "Unable to find an unassigned function app in region" deployment error

During `paconn create`, Power Platform may return:

```
Unable to find an unassigned function app in '<region>'
```

**Cause:** This is a known, intermittently occurring Microsoft Power Platform infrastructure issue. The connector requires a dedicated function app slot in your region.

**This error is often transient.** Wait a few minutes and run the installer again — it frequently resolves on its own. If the error persists after 24 hours, raise a **Microsoft support ticket** and ask them to provision a function app slot in your region. Once Microsoft resolves this on their side, rerunning the installer will succeed.

---

### 🖥️ Power Automate Flow Editor — Custom Connector Actions Not Displaying Correctly

There are currently active bugs in both Flow Editor v1 and v2 that can prevent custom connector actions from displaying properly in the action picker.

**Cause:** Acknowledged bugs in Power Automate. Microsoft is aware and working on a fix.

**Workaround:**
- Use **Flow Editor v1** (not v2)
- Do **not** click "See more" in the workflow action list — the actions that matter are already visible, and clicking "See more" may trigger the display issue. There is nothing additional to see there.

---

## Change Log

### v2.2.0

**New Operations**
- **Delete an Obligation** — removes an obligation (triggered instances delete immediately; recurring instances return a `409 Conflict` identifying the parent to delete instead)
- **List Obligation Types** — retrieves all obligation types configured in your Ironclad Data Manager, powering a dynamic dropdown for `obligationTypeKey`

**Webhook Trigger Events**
- Expanded the webhook `events` enum from 22 to 42 values, adding the `*` (all events) wildcard, signer/signature-packet events (`workflow_signer_added`, `workflow_signer_removed`, `workflow_signer_reassigned`, `workflow_signature_packet_fully_signed`, `workflow_signature_packet_signatures_collected`, `workflow_signature_packet_signer_first_viewed`, `workflow_signature_packet_signer_viewed`, `workflow_signature_packet_document_moved`), `workflow_changed_turn`, `workflow_step_updated`, `workflow_roles_assigned`, and the full Record & Obligation event category (`record_contract_status_changed`, `obligation_created`, `obligation_status_changed`, `obligation_due_date_changed`, `obligation_assignee_changed`, `obligation_updated`, `obligations_extraction_completed`)

**Connection Parameter Sets**
- Every environment (Global, EU1, Demo, Preview) and Client Credentials now offers 4 scope variants — Full, No Conversational Search, No Obligations, and No Conversational Search and Obligations — replacing the single Demo-only "No Conversational Search" variant

### v2.1.0

**New Operations**
- **Obligation Operations** — Create, Retrieve, Update, and List All Obligations (requires `public.obligations.*` scopes)
- **Conversational Search** — Search contracts and records using natural language (requires `public.search.conversational` scope — contact Ironclad to enable if not available in your instance)

**SCIM Improvements**
- Group PATCH `op` field now correctly limited to `add` and `remove` (SCIM spec compliant; `replace` is not supported by Ironclad groups)
- User PATCH now accepts any valid SCIM attribute path (previously restricted by enum)
- User POST/PUT: `password` field removed from required (not needed when SAML/SSO is configured)
- User POST/PUT: Added `active` field — POST defaults to `true` (optional); PUT exposes enum with deactivation warning label
- User POST/PUT: Added **Enterprise User Properties** array for IETF standard extension attributes (`department`, `division`, `organization`, `costCenter`, `employeeNumber`, `manager`)
- User POST/PUT: Added **Ironclad User Properties** array for custom Ironclad extension attributes (requires Ironclad Support enablement)

**Connection Parameter Sets**
- Added **Demo (No Conversational Search)** connection type — same as Demo but excludes `public.search.conversational` scope, for Ironclad instances where Conversational Search is not enabled
- Restored `public.search.conversational` scope to all other connection types

**Bug Fixes / Minor Changes**
- Removed enum restrictions on SCIM Group PATCH `path`, Obligation Type Key, and Obligation Filter property fields — custom values can now be entered freely

---

### Earlier

- Corrected the Swagger `RetrievePredictions` operation ID spelling and fixed the records export success description text from `Reecords Exported` to `Records Exported`.

---

## 🔧 Common Issues & Troubleshooting

### ❌ "Invalid callback URL" error when creating a connection

**Cause:** The callback URL was not properly configured in Ironclad.

**Solution:**
1. Did you get the callback URL from the installer output or from Power Platform as described in [Step 1 — Retrieving the callback URL manually](#retrieving-the-callback-url-manually)?
2. Did you click **Save** in the Ironclad app after adding the Redirect URI?

---

### ❌ "Invalid scope" error when creating a connection

**Cause:** Missing required scopes in your Ironclad application, or a scope that is not enabled for your Ironclad instance.

**Solution:**
- Ensure you have added **ALL** [required scopes](#-required-scopes) to your Ironclad app
- The connector requires all scopes to function properly — partial scope configuration is not supported
- **`public.search.conversational` scope error?** This scope is not available in all Ironclad instances. Contact [Ironclad Support](https://support.ironcladapp.com) to request enablement, or use the **No Conversational Search** variant of your environment's connection type to connect without this scope.
- **`public.obligations.*` scope error?** Obligations may not be enabled on your Ironclad plan. Use the **No Obligations** variant of your environment's connection type to connect without these scopes.

---

### ❌ "Invalid client/secret" error or red banner saying connection could not be created

**Cause:** Mismatch between connector configuration and Ironclad app settings.

**Solution - Check the following:**

<table style="width:100%">
<thead>
<tr><th>Check</th><th>Action</th></tr>
</thead>
<tbody>
<tr><td>Environment mismatch</td><td>Did you select the correct Ironclad environment (Global, EU1, Demo, Preview) in the connector? The environment must match where your app is registered.</td></tr>
<tr><td>Unsaved changes</td><td>Did you click <strong>Save</strong> in the Ironclad app after creating the app and generating the secret?</td></tr>
<tr><td>Accidental connector save</td><td>Did you accidentally save the custom connector when retrieving the callback URL from the Security tab? If so, you need to <strong>redeploy</strong> the connector — use the installer or the manual steps in <a href="#step-1️⃣-install-the-connector">Step 1</a>.</td></tr>
</tbody>
</table>

---

### ❌ "Action does not contain a valid OpenAPI schema object" when using connector actions

**Cause:** The custom script was not properly deployed with the connector.

**Solution:**
1. Open the custom connector in Power Platform
2. Go to the **4. Code** tab
3. Verify that the code is visible and the toggle is **switched ON**
4. If the code is missing, redeploy the connector using the installer or the manual `paconn` steps in [Step 1](#step-1️⃣-install-the-connector)

---

## ❓ Frequently Asked Questions

<details>
<summary><b>How do I create a workflow with multiple documents?</b></summary>

Use the asynchronous pattern:
1. Create the workflow asynchronously
2. Wait for completion (minimum 10 seconds)
3. Add documents using separate `Create Workflow Document` operations
</details>

<details>
<summary><b>What should I do if my workflow creation times out?</b></summary>

Always use the asynchronous workflow creation pattern for complex workflows or when attaching documents. The synchronous operation is only suitable for simple approval workflows.
</details>

<details>
<summary><b>How do I manage signatures programmatically?</b></summary>

For workflows in the Sign step:
1. **Check status** → `Retrieve Sign Status`
2. **Send for signature** → `Send Signature Request`
3. **Send reminders** → `Remind a Signer`
4. **Update signer info** → `Update a Signer`
5. **Cancel request** → `Cancel Signature Request`
6. **Generate links** → `Create Recipient URL` or `Create Embeddable Recipient URL`
</details>

---

<div align="center">

Made with ❤️ for the Ironclad & Power Platform community

</div>

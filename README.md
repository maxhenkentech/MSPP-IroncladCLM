# ⚖️ Ironclad CLM Custom Connector

A powerful custom connector for [Ironclad CLM](https://ironcladapp.com/), enabling seamless integration with Microsoft Power Platform (Power Automate, Power Apps, and Copilot Studio).

> 🔧 This connector uses a custom C# script to transform API traffic, making the non-OpenAPI compatible Ironclad API work seamlessly with OpenAPI standards.

---

## 📑 Table of Contents

- [Publisher](#-publisher)
- [Prerequisites](#-prerequisites)
- [Supported Operations](#-supported-operations)
- [Getting Started](#-getting-started)
- [Deployment Instructions](#-deployment-instructions)
- [Known Issues and Limitations](#️-known-issues-and-limitations)
- [Common Issues & Troubleshooting](#-common-issues--troubleshooting)
- [FAQ](#-frequently-asked-questions)

---

## 👤 Publisher

**Independent Publisher**
Maximilian Henkensiefken (Amadeus IT Group, S.A.) in collaboration with Ironclad

---

## ✅ Prerequisites

| Requirement | Description |
|-------------|-------------|
| 🔐 Ironclad Account | An Ironclad CLM account with APIs enabled (additional charges may apply) |
| 🔑 API Credentials | Client ID and Secret from a registered application in Ironclad Admin panel |
| 💼 Power Platform License | Licensed access to Power Automate, Power Apps, or Copilot Studio |

---

## 🔌 Supported Operations

Almost all Ironclad API operations are available. For complete API documentation, visit the [Ironclad Developer Center](https://developer.ironcladapp.com/docs/getting-started).

### 📋 Workflow Operations

| Operation | Description |
|-----------|-------------|
| List all Workflow Schemas | Returns a list of workflow schemas |
| Retrieve a Workflow Schema | Returns the fields used in the workflow's launch form |
| List all Workflows | List all workflows in your Ironclad account |
| Create a Workflow Synchronously | Launch a new workflow synchronously |
| Create a Workflow Asynchronously | Launch a new workflow asynchronously |
| Retrieve a Workflow | View the data associated with a specific workflow |
| Update Workflow Metadata | Update the attributes on a workflow in the Review step |
| Retrieve Async Workflow Status | Check the status of an asynchronously created workflow |
| List all Workflow Approvals | Returns a list of approvals for the workflow |
| Update Approval on a Workflow | Updates an approval to the specified status |
| Cancel Workflow | Cancel a workflow |
| Pause Workflow | Pause a workflow |
| Resume Workflow | Resume a workflow |
| Revert to Review Step | Reverts a workflow to the Review step |
| List All Workflow Signers | Returns a list of workflow signers and their signature status |
| Retrieve Approval Requests | Returns a list of workflow approval requests |
| List all Comments | Return a list of comments on a workflow |
| Create a Comment | Creates a comment in the workflow's activity feed |
| Retrieve a Comment | Return a single comment for a specified workflow |
| Retrieve a Workflow Document | Download a document associated with a specific workflow |
| Create a Workflow Document | Create a document in the specified workflow attribute |
| Retrieve Email Threads | List all email threads in the specified workflow |
| Retrieve an Email Thread | List a single email thread for a specified workflow |
| Retrieve Email Attachment | Retrieve an attachment from the specified email thread |
| List all Workflow Participants | Returns a list of workflow participants |
| Retrieve Turn History | An array of objects for each turn on a workflow |
| Create a Signed Document | Upload a signed document to a workflow in sign step |

### ✍️ Signature Operations

> 🧪 **Note:** Operations marked with 🧪 are untested (Ironclad Signature not available to publisher).

| Operation | Description |
|-----------|-------------|
| Retrieve Sign Status | Returns sign status information for a workflow in the sign step |
| Send Signature Request | Send a signature packet out for signature |
| Cancel Signature Request | Cancel a signature request that was out for signature |
| Update a Signer | Update a signer's details (email, name) |
| Delete a Signer | Remove a signer from a signature packet ⚠️ *Cancels current packet* |
| Remind a Signer | Send a reminder to a signer |
| 🧪 Create Recipient URL | Create a recipient URL for signature access *(Ironclad Signature only)* |
| 🧪 Create Embeddable Recipient URL | Create an embeddable URL for iframe integration *(Ironclad Signature only)* |

### 📤 Data Export Operations

> 💰 *Requires Security & Data Pro add-on*

| Operation | Description |
|-----------|-------------|
| Create a Data Export Job | Submit a request to generate a new data export |
| Retrieve Data Export Job Status | Check the status of a data export job |
| Download Data Export File | Download the completed data export file |

### 📁 Record Operations

| Operation | Description |
|-----------|-------------|
| List All Records | View all records in the company, with filtering available |
| Create a Record | Create a contract record with specified metadata properties |
| Retrieve XLSX Export | Export a records report with filtering options |
| Retrieve Record Schemas | View the schema associated with contract records |
| Retrieve Predictions | Get status of predictions for smart import records |
| Create Smart Import Record | Upload a file to create a record with smart import |
| Upload to Existing Import | Add a file to an existing import |
| Retrieve Record | View a specific record and its associated data |
| Replace a Record | Update an existing record with new metadata |
| Update Record Metadata | Update specific fields on a record |
| Delete a Record | Delete an existing record |
| Retrieve Record Signed Copy | View the signed copy of a specific record |
| Create Record Signed Copy | Create a signed copy for a specific record |
| Remove Record Signed Copy | Remove the signed copy from a specific record |
| Retrieve Attachment | View an attachment on a specific record |
| Create Attachment | Create an attachment for a specific record |
| Delete Attachment | Remove an attachment from a specific record |

### 👥 User & Group Operations (SCIM)

| Operation | Users | Groups |
|-----------|-------|--------|
| List | ✅ | ✅ |
| Create | ✅ | ✅ |
| Retrieve | ✅ | ✅ |
| Replace | ✅ | ✅ |
| Update | ✅ | ✅ |
| Delete | ✅ | ✅ |

---

## 🚀 Getting Started

### 🌍 Supported Environments

| Environment | URL | Description |
|-------------|-----|-------------|
| 🌐 Global | `ironcladapp.com` | Production (majority of customers) |
| 🇪🇺 EU1 | `eu1.ironcladapp.com` | EU Production |
| 🧪 Demo | `demo.ironcladapp.com` | Sandbox environment |
| 🔮 Preview | `preview.ironcladapp.com` | Preview features |

> ⚠️ Each environment requires separate client application registration.

### 🔐 Obtaining Credentials

1. Log in to your Ironclad account
2. Navigate to **Company Settings** > **API** tab
3. Click **Create new app**
4. Configure:
   - Title and Description
   - Grant Types → Select **Authorization Code**
   - Redirect URIs
   - Required Resource Scopes
5. Save the **Client ID** and **Client Secret** securely

📚 For more details, visit the [Ironclad Developer Hub - API Authentication](https://developer.ironcladapp.com/reference/authentication-api).

---

## 📦 Deployment Instructions

### Prerequisites

- [Microsoft Power Platform CLI (paconn)](https://learn.microsoft.com/en-us/connectors/custom-connectors/paconn-cli) installed
- Access to a Power Platform environment with custom connector permissions
- An Ironclad account with API access enabled

### Step 1️⃣: Deploy the Custom Connector

Open a terminal and navigate to the connector directory:

```bash
cd "Custom Connectors/Ironclad CLM"
```

Log in to Power Platform:

```bash
paconn login
```

Deploy the custom connector:

```bash
paconn create --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx --icon icon.png --secret dummy
```

> 💡 The `--secret dummy` parameter is a placeholder. The actual client secret will be configured when creating a connection.

### Step 2️⃣: Retrieve the Callback URL

After deploying, retrieve the OAuth callback URL to configure in Ironclad:

1. Open [Power Automate](https://make.powerautomate.com) or [Power Apps](https://make.powerapps.com)
2. Navigate to **Custom Connectors**
3. Find and open **Ironclad CLM**
4. Go to the **Security** tab
5. Click **Edit**
6. Ensure **OAuth 2.0** is selected
7. Copy the **Redirect URL** at the bottom

![Copy Redirect URL from Power Platform](screenshots/Copy%20Redirect%20URL.png)

> ⚠️ **CRITICAL:** Leave this page **WITHOUT SAVING**. Do not click "Update connector". Simply close the tab after copying the URL.

### Step 3️⃣: Configure the Ironclad Application

1. Log in to your Ironclad account
2. Navigate to **Company Settings** > **API** tab
3. Click **Create new app** (or edit existing)
4. Configure:
   - **Title**: e.g., "Power Platform Connector"
   - **Grant Types**: Select "Authorization Code"
   - **Redirect URIs**: Paste the callback URL from Step 2
   - **Resource Scopes**: Select all [required scopes](#-required-scopes)
5. Save and securely store the **Client ID** and **Client Secret**

![Paste Redirect URL in Ironclad](screenshots/Paste%20Redirect%20URL.png)

### Step 4️⃣: Create a Connection

1. Return to Power Automate or Power Apps
2. Create a new flow/app using the Ironclad CLM connector
3. When prompted:
   - Select your Ironclad instance (Global, EU1, Demo, or Preview)
   - Enter your **Client ID**
   - Enter your **Client Secret**
4. Complete the OAuth authorization flow

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

---

## ⚠️ Known Issues and Limitations

### 📝 Complex Data Types in Updates

When updating workflow or record metadata, use the raw data format:

| Type | ❌ Don't Use | ✅ Use |
|------|-------------|--------|
| Monetary | "EUR 1,598.12" | `{"currency": "EUR", "amount": "1598.12"}` |
| Date | "31st January 2024" | `"2024-01-31T00:00:00Z"` |
| Address | Single line | Use `\n` for line breaks |

> This only applies to update operations, not reads.

### ⏱️ Synchronous Workflow Creation

The synchronous workflow creation has limitations:

- ✅ Works for simple workflows with basic approvers
- ❌ Often fails for complex workflows (5-second timeout)

**Recommended Pattern for Complex Workflows:**
1. Use `Create a Workflow Asynchronously`
2. Add a delay (10+ seconds)
3. Check status with `Retrieve Async Workflow Status`
4. Get details with `Retrieve a Workflow`
5. Add documents with `Create a Workflow Document`

### 🔄 Dynamic Schema Limitations

- Approver lists require explicit workflow specification
- Dynamic workflow IDs prevent schema fetching at runtime
- Affects operations like `Update Approval on a Workflow`

### ✍️ Signature Operations

> 🧪 **Note:** The Ironclad Signature-specific features (Recipient URLs, Embedded URLs) have not been tested as the publisher does not have access to Ironclad Signature. These operations are implemented based on API documentation. Please report any issues.

| Consideration | Details |
|---------------|---------|
| Workflow Step | Must be in **Sign** step |
| Delete Signer | ⚠️ Cancels current signature packet |
| Adobe Sign | Reminding one signer reminds ALL; updating cancels request |
| Embedded URLs | Ironclad Signature only (not Adobe Sign/DocuSign) |
| Tag Requirements | All signers need signature/initials tags before sending |

### 📤 Data Export

- 💰 Requires paid **Security & Data Pro** add-on
- ⏳ Asynchronous process - poll status before downloading

---

## 🔧 Common Issues & Troubleshooting

### ❌ "Invalid callback URL" error when creating a connection

**Cause:** The callback URL was not properly configured in Ironclad.

**Solution:**
1. Did you get the callback URL from Power Platform as described in [Step 2](#step-2️⃣-retrieve-the-callback-url)?
2. Did you click **Save** in the Ironclad app after adding the Redirect URI?

---

### ❌ "Invalid scope" error when creating a connection

**Cause:** Missing required scopes in your Ironclad application.

**Solution:**
- Ensure you have added **ALL** [required scopes](#-required-scopes) to your Ironclad app
- The connector requires all scopes to function properly - partial scope configuration is not supported

---

### ❌ "Invalid client/secret" error or red banner saying connection could not be created

**Cause:** Mismatch between connector configuration and Ironclad app settings.

**Solution - Check the following:**

| Check | Action |
|-------|--------|
| Environment mismatch | Did you select the correct Ironclad environment (Global, EU1, Demo, Preview) in the connector? The environment must match where your app is registered. |
| Unsaved changes | Did you click **Save** in the Ironclad app after creating the app and generating the secret? |
| Accidental connector save | Did you accidentally save the custom connector when retrieving the callback URL from the Security tab? If so, you need to **redeploy** the connector using the steps in [Step 1](#step-1️⃣-deploy-the-custom-connector). |

---

### ❌ "Action does not contain a valid OpenAPI schema object" when using connector actions

**Cause:** The custom script was not properly deployed with the connector.

**Solution:**
1. Open the custom connector in Power Platform
2. Go to the **4. Code** tab
3. Verify that the code is visible and the toggle is **switched ON**
4. If you cannot see the code, redeploy the connector with the `--script` parameter:
   ```bash
   paconn create --api-def apiDefinition.swagger.json --api-prop apiProperties.json --script script.csx --icon icon.png --secret dummy
   ```

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

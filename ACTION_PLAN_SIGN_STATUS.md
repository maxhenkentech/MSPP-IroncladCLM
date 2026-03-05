# Action Plan: Add Sign Status API Endpoints

## Overview

Add 7 new API endpoints for signature management to the Ironclad CLM custom connector.

---

## Endpoints to Add

| # | Method | Path | OperationId (proposed) |
|---|--------|------|------------------------|
| 1 | DELETE | `/workflows/{id}/sign-status/signers/{signerRoleName}` | `DeleteWorkflowSigner` |
| 2 | PATCH | `/workflows/{id}/sign-status/signers/{signerRoleName}` | `UpdateWorkflowSigner` |
| 3 | POST | `/workflows/{id}/sign-status/signers/{signerRoleName}/remind` | `RemindWorkflowSigner` |
| 4 | POST | `/workflows/{id}/sign-status/cancel-signature-request` | `CancelSignatureRequest` |
| 5 | POST | `/workflows/{id}/sign-status/send-signature-request` | `SendSignatureRequest` |
| 6 | GET | `/workflows/{id}/sign-status` | `RetrieveSignStatus` |
| 7 | POST | `/workflows/{id}/signatures/recipient-urls/embedded` | `CreateEmbeddedRecipientUrl` |

---

## API Specifications Retrieved from Ironclad Documentation

### 1. DELETE /workflows/{id}/sign-status/signers/{signerRoleName}

**Purpose:** Remove a signer from a signature packet in a workflow.

**OAuth Scope:** `public.workflows.deleteSigners`

**Documentation:** https://developer.ironcladapp.com/reference/delete-signer

| Parameter | Location | Type | Required | Description |
|-----------|----------|------|----------|-------------|
| id | path | string | yes | Workflow ID or Ironclad ID |
| signerRoleName | path | string | yes | Role name of signer to remove |
| x-as-user-email | header | string | no | Actor email for permissions |
| x-as-user-id | header | string | no | Actor user ID |

**Request Body:** None

**Response:** 200 OK (confirmation)

**Important:** Removing a signer will **cancel** the current signature packet that is out for signatures.

---

### 2. PATCH /workflows/{id}/sign-status/signers/{signerRoleName}

**Purpose:** Update a signer's details (email, name, or reassign to different person).

**OAuth Scope:** `public.workflows.updateSigners`

**Documentation:** https://developer.ironcladapp.com/reference/update-signer

| Parameter | Location | Type | Required | Description |
|-----------|----------|------|----------|-------------|
| id | path | string | yes | Workflow ID or Ironclad ID |
| signerRoleName | path | string | yes | Role name of signer to update |
| x-as-user-email | header | string | no | Actor email for permissions |
| x-as-user-id | header | string | no | Actor user ID |
| body | body | object | yes | Updated signer details |

**Request Body Schema:**
```json
{
  "email": "string (optional - new email address)",
  "name": "string (optional - new display name)"
}
```

**Response:** 200 OK with updated signer information

**Constraints:**
- Editing a signer will **NOT** cancel a signature request unless using Adobe Sign
- Cannot update signers who have already signed via the API
- Original signer receives email notification of reassignment if packet was sent
- Only available during the signature step

---

### 3. POST /workflows/{id}/sign-status/signers/{signerRoleName}/remind

**Purpose:** Send a reminder to a signer who hasn't signed yet.

**OAuth Scope:** `public.workflows.remindSigners`

**Documentation:** https://developer.ironcladapp.com/reference/remind-signer

| Parameter | Location | Type | Required | Description |
|-----------|----------|------|----------|-------------|
| id | path | string | yes | Workflow ID or Ironclad ID |
| signerRoleName | path | string | yes | Role name of signer to remind |
| x-as-user-email | header | string | no | Actor email for permissions |
| x-as-user-id | header | string | no | Actor user ID |

**Request Body:** None

**Response:** 200 OK

**Constraints:**
- Can only send reminder after signature packet has been sent out
- With Adobe Sign: reminding one signer reminds ALL unsigned signers

---

### 4. POST /workflows/{id}/sign-status/cancel-signature-request

**Purpose:** Cancel a signature request that was sent out for signature.

**OAuth Scope:** `public.workflows.cancelSignatureRequests`

**Documentation:** https://developer.ironcladapp.com/reference/cancel-signature-request

| Parameter | Location | Type | Required | Description |
|-----------|----------|------|----------|-------------|
| id | path | string | yes | Workflow ID or Ironclad ID |
| x-as-user-email | header | string | no | Actor email for permissions |
| x-as-user-id | header | string | no | Actor user ID |

**Request Body:** None

**Response:** 200 OK

---

### 5. POST /workflows/{id}/sign-status/send-signature-request

**Purpose:** Send a signature packet out for signature.

**OAuth Scope:** `public.workflows.sendSignatureRequests`

**Documentation:** https://developer.ironcladapp.com/reference/send-signature-request

| Parameter | Location | Type | Required | Description |
|-----------|----------|------|----------|-------------|
| id | path | string | yes | Workflow ID or Ironclad ID |
| x-as-user-email | header | string | no | Actor email for permissions |
| x-as-user-id | header | string | no | Actor user ID |

**Request Body:** None

**Response:** 200 OK

**Constraint:** All signers must have an associated signature or initials tag before sending (tag requirements depend on signature provider).

---

### 6. GET /workflows/{id}/sign-status

**Purpose:** Get sign status information for a workflow in the sign step.

**OAuth Scope:** `public.workflows.readSignStatus`

**Documentation:** https://developer.ironcladapp.com/reference/get-sign-status

| Parameter | Location | Type | Required | Description |
|-----------|----------|------|----------|-------------|
| id | path | string | yes | Workflow ID or Ironclad ID |
| x-as-user-email | header | string | no | Actor email for permissions |
| x-as-user-id | header | string | no | Actor user ID |

**Response Schema (inferred from similar endpoints):**
```json
{
  "workflowId": "string",
  "status": "string",
  "signers": [
    {
      "email": "string",
      "displayName": "string",
      "roleName": "string",
      "routingOrder": "integer",
      "status": "string",
      "signedAt": "string (ISO 8601)",
      "sentAt": "string (ISO 8601)",
      "expiresAt": "string (ISO 8601)"
    }
  ],
  "sentAt": "string (ISO 8601)",
  "completedAt": "string (ISO 8601)"
}
```

**Constraint:** Workflow must be in the sign step.

---

### 7. POST /workflows/{id}/signatures/recipient-urls/embedded

**Purpose:** Create an embeddable recipient URL for a signer/viewer to access signature request within an iframe.

**OAuth Scope:** `public.workflows.createEmbeddableSignerUrls`

**Documentation:** https://developer.ironcladapp.com/reference/create-embeddable-recipient-url

| Parameter | Location | Type | Required | Description |
|-----------|----------|------|----------|-------------|
| id | path | string | yes | Workflow ID or Ironclad ID |
| x-as-user-email | header | string | no | Actor email for permissions |
| x-as-user-id | header | string | no | Actor user ID |
| body | body | object | yes | Recipient details |

**Request Body (based on existing `CreateSignatureRecipientUrl` pattern):**
```json
{
  "type": "signer | viewer",
  "roleName": "string"
}
```

**Response:**
```json
{
  "recipientUrl": "string",
  "embeddedUrl": "string"
}
```

**Note:** This functionality is exclusive to Ironclad Signature (not Adobe Sign or DocuSign).

---

## Dynamic Lists Strategy

### Path Parameters with Dynamic Values

All `{}` path parameters must be retrievable dynamically from other operations:

#### 1. Workflow ID (`{Workflow}`)
**Reuse existing `workflowId_path` parameter** - Already configured with dynamic values:
```json
"x-ms-dynamic-values": {
    "operationId": "ListAllWorkflows",
    "parameters": {
        "page": 0,
        "pageSize": 100,
        "sortDirection": "DESC",
        "sortField": "lastUpdated"
    },
    "value-collection": "list",
    "value-path": "id",
    "value-title": "label"
}
```

#### 2. Signer Role Name (`{SignerRole}`) - NEW
**Create new `signerRoleName_path` parameter** with dynamic values dependent on Workflow:
```json
"x-ms-dynamic-values": {
    "operationId": "ListWorkflowSignatures",
    "parameters": {
        "Workflow": {
            "parameter": "Workflow"
        }
    },
    "value-collection": "signers",
    "value-path": "roleName",
    "value-title": "roleName"
}
```

This pattern follows the existing `workflowRoleID_path` parameter which uses:
- `"parameter": "Workflow"` to reference the Workflow path parameter from the same operation
- The dependent parameter dropdown will only populate after Workflow is selected

### Body Parameters with Dynamic Values

The `roleName` property in request bodies must also be dynamically populated based on the selected Workflow:

#### 1. NEW: Embedded Recipient URL Body (`embeddedRecipientUrlPost_body`)
The `roleName` property must have dynamic values referencing the Workflow path parameter:
```json
"roleName": {
    "type": "string",
    "title": "Role Name",
    "description": "The name of the signer or viewer role.",
    "x-ms-visibility": "important",
    "x-ms-dynamic-values": {
        "operationId": "ListWorkflowSignatures",
        "parameters": {
            "Workflow": {
                "parameter": "Workflow"
            }
        },
        "value-collection": "signers",
        "value-path": "roleName",
        "value-title": "roleName"
    }
}
```

#### 2. EXISTING: Update `recipientUrlPost_body` (Enhancement)
**REQUIRED:** Update the existing `recipientUrlPost_body` parameter (used by `CreateSignatureRecipientUrl`) to add dynamic values for `roleName`:

**Current (no dynamic values):**
```json
"roleName": {
    "type": "string",
    "title": "Role Name",
    "description": "The name of the signer or viewer role.",
    "x-ms-visibility": "important"
}
```

**Updated (with dynamic values):**
```json
"roleName": {
    "type": "string",
    "title": "Role Name",
    "description": "The name of the signer or viewer role.",
    "x-ms-visibility": "important",
    "x-ms-dynamic-values": {
        "operationId": "ListWorkflowSignatures",
        "parameters": {
            "Workflow": {
                "parameter": "Workflow"
            }
        },
        "value-collection": "signers",
        "value-path": "roleName",
        "value-title": "roleName"
    }
}
```

This ensures users can select from available signers in a dropdown instead of typing the role name manually.

---

## Parameter Usage Summary

### New Operations

| Operation | Path Params | Body Params | Dynamic Sources |
|-----------|-------------|-------------|-----------------|
| RetrieveSignStatus | `{Workflow}` | - | `ListAllWorkflows` |
| SendSignatureRequest | `{Workflow}` | - | `ListAllWorkflows` |
| CancelSignatureRequest | `{Workflow}` | - | `ListAllWorkflows` |
| RemindWorkflowSigner | `{Workflow}`, `{SignerRole}` | - | `ListAllWorkflows` → `ListWorkflowSignatures` |
| UpdateWorkflowSigner | `{Workflow}`, `{SignerRole}` | email, name | `ListAllWorkflows` → `ListWorkflowSignatures` |
| DeleteWorkflowSigner | `{Workflow}`, `{SignerRole}` | - | `ListAllWorkflows` → `ListWorkflowSignatures` |
| CreateEmbeddedRecipientUrl | `{Workflow}` | type, `roleName` | `ListAllWorkflows` → `ListWorkflowSignatures` |

### Existing Operation Enhancement

| Operation | Body Param to Update | Dynamic Source |
|-----------|---------------------|----------------|
| CreateSignatureRecipientUrl | `roleName` | `ListWorkflowSignatures` (depends on `{Workflow}`) |

**Dependency Chain:**
- `{Workflow}` → populated from `ListAllWorkflows`
- `{SignerRole}` and body `roleName` → populated from `ListWorkflowSignatures` after Workflow is selected

---

## Implementation Tasks

### 1. apiDefinition.swagger.json Changes

#### 1.1 Add New Path Definitions

Add these new paths under `/paths`:

```
/public/api/v1/workflows/{Workflow}/sign-status
  - GET: RetrieveSignStatus

/public/api/v1/workflows/{Workflow}/sign-status/signers/{SignerRole}
  - DELETE: DeleteWorkflowSigner
  - PATCH: UpdateWorkflowSigner

/public/api/v1/workflows/{Workflow}/sign-status/signers/{SignerRole}/remind
  - POST: RemindWorkflowSigner

/public/api/v1/workflows/{Workflow}/sign-status/cancel-signature-request
  - POST: CancelSignatureRequest

/public/api/v1/workflows/{Workflow}/sign-status/send-signature-request
  - POST: SendSignatureRequest

/public/api/v1/workflows/{Workflow}/signatures/recipient-urls/embedded
  - POST: CreateEmbeddedRecipientUrl
```

#### 1.2 Add New Parameters

Create reusable parameter definitions in the `parameters` section:

**`signerRoleName_path`** - Path parameter for signer role with dynamic values (follows `workflowRoleID_path` pattern):
```json
"signerRoleName_path": {
    "description": "The role name of the signer.",
    "in": "path",
    "name": "SignerRole",
    "required": true,
    "type": "string",
    "x-ms-summary": "Signer Role",
    "x-ms-url-encoding": "single",
    "x-ms-visibility": "important",
    "x-ms-dynamic-values": {
        "operationId": "ListWorkflowSignatures",
        "parameters": {
            "Workflow": {
                "parameter": "Workflow"
            }
        },
        "value-collection": "signers",
        "value-path": "roleName",
        "value-title": "roleName"
    }
}
```

**`updateSignerPost_body`** - Request body for PATCH update signer:
```json
"updateSignerPost_body": {
    "description": "The signer update object.",
    "in": "body",
    "name": "body",
    "required": true,
    "schema": {
        "type": "object",
        "properties": {
            "email": {
                "type": "string",
                "title": "Email",
                "description": "The new email address for the signer.",
                "x-ms-visibility": "important"
            },
            "name": {
                "type": "string",
                "title": "Name",
                "description": "The new display name for the signer.",
                "x-ms-visibility": "important"
            }
        }
    },
    "x-ms-visibility": "important"
}
```

**`embeddedRecipientUrlPost_body`** - Request body for embedded URL with dynamic roleName:
```json
"embeddedRecipientUrlPost_body": {
    "description": "The embedded recipient URL request object.",
    "in": "body",
    "name": "body",
    "required": true,
    "schema": {
        "type": "object",
        "required": ["type", "roleName"],
        "properties": {
            "type": {
                "type": "string",
                "title": "Recipient Type",
                "description": "The type of recipient accessing the signature request.",
                "enum": ["signer", "viewer"],
                "x-ms-visibility": "important"
            },
            "roleName": {
                "type": "string",
                "title": "Role Name",
                "description": "The name of the signer or viewer role.",
                "x-ms-visibility": "important",
                "x-ms-dynamic-values": {
                    "operationId": "ListWorkflowSignatures",
                    "parameters": {
                        "Workflow": {
                            "parameter": "Workflow"
                        }
                    },
                    "value-collection": "signers",
                    "value-path": "roleName",
                    "value-title": "roleName"
                }
            }
        }
    },
    "x-ms-visibility": "important"
}
```

#### 1.3 Update Existing Parameter

**`recipientUrlPost_body`** - Add dynamic values to existing parameter's `roleName` property:

**Location:** Line ~8142 in `apiDefinition.swagger.json`

**Change:** Add `x-ms-dynamic-values` to the `roleName` property:
```json
"roleName": {
    "type": "string",
    "title": "Role Name",
    "description": "The name of the signer or viewer role.",
    "x-ms-visibility": "important",
    "x-ms-dynamic-values": {
        "operationId": "ListWorkflowSignatures",
        "parameters": {
            "Workflow": {
                "parameter": "Workflow"
            }
        },
        "value-collection": "signers",
        "value-path": "roleName",
        "value-title": "roleName"
    }
}
```

#### 1.4 Add New Definitions

**`signStatus_object`** - Full sign status response:
```json
{
    "description": "The sign status of a workflow.",
    "type": "object",
    "properties": {
        "workflowId": { "$ref": "#/definitions/uniqueId_string" },
        "status": {
            "type": "string",
            "title": "Status",
            "description": "The current signature status.",
            "x-ms-visibility": "important"
        },
        "signers": { "$ref": "#/definitions/workflowSigners_array" },
        "sentAt": {
            "type": "string",
            "title": "Sent At",
            "description": "When the signature request was sent.",
            "x-ms-visibility": "important"
        },
        "completedAt": {
            "type": "string",
            "title": "Completed At",
            "description": "When all signatures were collected.",
            "x-ms-visibility": "advanced"
        }
    },
    "title": "Sign Status",
    "x-ms-visibility": "important"
}
```

**`embeddedRecipientUrl_object`** - Embedded URL response:
```json
{
    "description": "The embedded recipient URL object.",
    "type": "object",
    "properties": {
        "recipientUrl": {
            "type": "string",
            "title": "Recipient URL",
            "description": "The shareable URL for the signer or viewer.",
            "x-ms-visibility": "important"
        },
        "embeddedUrl": {
            "type": "string",
            "title": "Embedded URL",
            "description": "The embeddable URL for iframe integration.",
            "x-ms-visibility": "important"
        }
    },
    "title": "Embedded Recipient URL",
    "x-ms-visibility": "important"
}
```

---

### 2. script.csx Changes

Evaluate if any response transformations are required:

| Operation | Transform Needed? | Notes |
|-----------|-------------------|-------|
| DeleteWorkflowSigner | No | Simple DELETE, minimal response |
| UpdateWorkflowSigner | Maybe | May return updated signer - evaluate response |
| RemindWorkflowSigner | No | Simple POST, minimal response |
| CancelSignatureRequest | No | Simple POST |
| SendSignatureRequest | No | Simple POST |
| RetrieveSignStatus | Maybe | May need signer array transformation for consistency |
| CreateEmbeddedRecipientUrl | No | Same pattern as existing CreateSignatureRecipientUrl |

**Recommendation:** Start without transformations, test against live API, then add transformations if needed for consistency with existing patterns.

---

### 3. apiProperties.json Changes

**Status: NO CHANGES REQUIRED**

All required OAuth scopes are already present in all environment configurations:

| Scope | Present | Used By |
|-------|---------|---------|
| `public.workflows.readSignStatus` | Yes | RetrieveSignStatus |
| `public.workflows.sendSignatureRequests` | Yes | SendSignatureRequest |
| `public.workflows.cancelSignatureRequests` | Yes | CancelSignatureRequest |
| `public.workflows.deleteSigners` | Yes | DeleteWorkflowSigner |
| `public.workflows.updateSigners` | Yes | UpdateWorkflowSigner |
| `public.workflows.remindSigners` | Yes | RemindWorkflowSigner |
| `public.workflows.createEmbeddableSignerUrls` | Yes | CreateEmbeddedRecipientUrl |

Verified in all 5 connection parameter sets:
- Default (demo)
- oauthUS (Global)
- oauthPREVIEW (Preview)
- oauthDEMO (Demo)
- oauthEU1 (EU1)
- ClientCredentials

---

## Testing Checklist

- [ ] Each endpoint can be called from Power Automate
- [ ] Dynamic dropdowns populate correctly for workflow and signer selection
- [ ] Error responses are properly formatted
- [ ] Headers (x-as-user-email, x-as-user-id) work as expected
- [ ] Works across all environments (Global, EU1, Demo, Preview)
- [ ] PATCH update signer correctly updates signer info
- [ ] DELETE signer correctly cancels signature packet (verify warning behavior)
- [ ] Remind signer sends email notification
- [ ] Embedded URL works for iframe integration (Ironclad Signature only)

---

## Implementation Order (Recommended)

1. **RetrieveSignStatus** - Foundation endpoint to get current sign status
2. **SendSignatureRequest** - Simple POST, no request body
3. **CancelSignatureRequest** - Simple POST, no request body
4. **RemindWorkflowSigner** - Simple POST with signer role path param
5. **UpdateWorkflowSigner** - PATCH with request body
6. **DeleteWorkflowSigner** - DELETE with signer role path param
7. **CreateEmbeddedRecipientUrl** - POST with request body (similar to existing)

---

## References

- [Delete Signer](https://developer.ironcladapp.com/reference/delete-signer)
- [Update Signer](https://developer.ironcladapp.com/reference/update-signer)
- [Remind Signer](https://developer.ironcladapp.com/reference/remind-signer)
- [Cancel Signature Request](https://developer.ironcladapp.com/reference/cancel-signature-request)
- [Send Signature Request](https://developer.ironcladapp.com/reference/send-signature-request)
- [Get Sign Status](https://developer.ironcladapp.com/reference/get-sign-status)
- [Create Embeddable Recipient URL](https://developer.ironcladapp.com/reference/create-embeddable-recipient-url)
- [List All Workflow Signers](https://developer.ironcladapp.com/reference/list-all-workflow-signers)

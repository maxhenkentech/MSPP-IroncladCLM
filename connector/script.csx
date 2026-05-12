using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.IO;
using System.Web;

/// <summary>
/// Compatibility-first Power Platform custom connector script for Ironclad.
///
/// This file intentionally preserves the current connector contract exactly as it is today:
/// - the same operation IDs are routed the same way
/// - the same request and response payload shapes are produced
/// - the same connector-specific quirks are kept when callers may already depend on them
///
/// The script lifecycle is:
/// 1. Optionally prepare extra context before the main API call.
/// 2. Rewrite the outgoing request for operations that need Power Platform friendly inputs.
/// 3. Send the request to Ironclad.
/// 4. Rewrite the successful response back into the shape expected by the connector.
/// </summary>
public class Script : ScriptBase
{
    private const string RecordPropertiesQueryParameter = "recordProperties";

    // SCIM patch operations require this exact schema marker in the request body.
    private const string ScimPatchOperationSchema =
        "urn:ietf:params:scim:api:messages:2.0:PatchOp";

    private JObject recordSchemaInfo;

    /// <summary>
    /// Main Power Platform script entry point.
    /// It preserves the current sequence of request preparation, downstream execution,
    /// and response post-processing for every operation.
    /// </summary>
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        if ("RetrieveRecord".Equals(this.Context.OperationId, StringComparison.OrdinalIgnoreCase))
        {
            await rtrRcd_RetrieveRecordSchemaInformation().ConfigureAwait(false);
        }

        await this.UpdateRequest().ConfigureAwait(false);
        var response = await this.Context
            .SendAsync(this.Context.Request, this.CancellationToken)
            .ConfigureAwait(false);

        // -------------------------------------------------------------------------
        // Transform 400 errors for CreateWorkflow if MISSING_PARAM + approvers
        // -------------------------------------------------------------------------
        if (!response.IsSuccessStatusCode 
            && response.StatusCode == HttpStatusCode.BadRequest
            && "CreateWorkflow".Equals(this.Context.OperationId, StringComparison.OrdinalIgnoreCase))
        {
            response = await TransformCreateWorkflowErrorResponseAsync(response).ConfigureAwait(false);
        }
        // -------------------------------------------------------------------------

        if (response.IsSuccessStatusCode)
        {
            await this.UpdateResponse(response).ConfigureAwait(false);
        }
        return response;
    }

    /// <summary>
    /// Applies request-side rewrites for operations whose Power Platform input shape
    /// differs from the raw Ironclad API shape.
    /// </summary>
    private async Task UpdateRequest()
    {
        switch (this.Context.OperationId)
        {
            case "CreateSignedCopyAttachment":
            case "CreateAttachment":
                await crtAtt_TransformToMultipartRequestForRecords().ConfigureAwait(false);
                break;
            case "CreateWorkflow":
                await crtWfl_TransformToMultipartRequestForWorkflows().ConfigureAwait(false);
                break;
            case "CreateWorkflowAsync":
                await crtAsyncWfl_TransformRequestJson().ConfigureAwait(false);
                break;
            case "CreateRecord":
            case "ReplaceRecord":
                await crtRcd_TransformCreateRecordRequest().ConfigureAwait(false);
                break;
            case "UpdateRecordMetadata":
                await updRcd_TransformUpdateRecordMetadataRequest().ConfigureAwait(false);
                break;
            case "CreateWorkflowDocument":
                await crtWflDoc_TransformToMultipartRequest().ConfigureAwait(false);
                break;
            case "UpdateGroup":
                await updGrp_TransformUpdateGroupRequest().ConfigureAwait(false);
                break;
            case "UpdateUser":
                await updUsr_TransformUpdateUserRequest().ConfigureAwait(false);
                break;
            case "GetEntityRelationshipType":
                await this.getEntRltTyp_TransformRequest().ConfigureAwait(false);
                break;
            case "RetrieveFormattedRecordSchema":
                await this.rtrRcdFmtSch_TransformRequest().ConfigureAwait(false);
                break;
            case "CreateEntity":
                await crtEnt_TransformCreateEntityRequest().ConfigureAwait(false);
                break;
            case "UpdateEntity":
                await updEnt_TransformUpdateEntityRequest().ConfigureAwait(false);
                break;
            case "UpdateWorkflowMetadata":
                await updWflMd_TransformUpdateWorkflowMetadataRequest().ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Applies response-side rewrites for operations that expose a connector-specific
    /// response shape instead of the raw Ironclad payload.
    /// </summary>
    private async Task UpdateResponse(HttpResponseMessage response)
    {
        switch (this.Context.OperationId)
        {
            case "ListUsers":
                await this.TransformResponseJsonBody(this.lstUsr_TransformUsersList, response)
                    .ConfigureAwait(false);
                break;
            case "ListWorkflowSchemas":
                await this.TransformResponseJsonBody(
                        this.lstWflSch_TransformListWorkflowSchemas,
                        response
                    )
                    .ConfigureAwait(false);
                break;
            case "RetrieveWorkflowSchema":
                await this.TransformResponseJsonBody(
                        this.rtrWflSch_TransformRetrieveWorkflowSchema,
                        response
                    )
                    .ConfigureAwait(false);
                break;
            case "RetrieveWorkflow":
                await this.TransformResponseJsonBody(
                        this.rtrWfl_TransformRetrieveWorkflow,
                        response
                    )
                    .ConfigureAwait(false);
                break;
            case "RetrieveRecord":
                await this.TransformResponseJsonBody(this.rtrRcd_TransformRetrieveRecord, response)
                    .ConfigureAwait(false);
                break;
            case "RetrieveEmailThread":
                await this.TransformResponseJsonBody(
                        this.rtrEml_TransformRetrieveEmailThread,
                        response
                    )
                    .ConfigureAwait(false);
                break;
            case "CreateRecord":
            case "ReplaceRecord":
            case "UpdateRecordMetadata":
                await this.TransformResponseJsonBody(
                        this.crtRcd_TransformCreateRecordResponse,
                        response
                    )
                    .ConfigureAwait(false);
                break;
            case "ListAllRecords":
                var listAllRecordsQuery = GetRequestQueryValue(RecordPropertiesQueryParameter);
                await this.TransformResponseJsonBody(
                        body =>
                            lstAllRcd_TransformListAllRecordsResponse(body, listAllRecordsQuery),
                        response
                    )
                    .ConfigureAwait(false);
                break;
            case "RetrieveRecordSchemas":
                var retrieveRecordSchemasQuery = GetRequestQueryValue(
                    RecordPropertiesQueryParameter
                );
                await this.TransformResponseJsonBody(
                        body =>
                            rtrRcdSch_TransformRetrieveRecordSchemas(
                                body,
                                retrieveRecordSchemasQuery,
                                false
                            ),
                        response
                    )
                    .ConfigureAwait(false);
                break;
            case "RetrieveFormattedRecordSchema":
                await this.rtrRcdFmtSch_TransformResponse(response).ConfigureAwait(false);
                break;
            case "ListAllWorkflows":
                await this.TransformResponseJsonBody(
                        this.lstAllWfl_TransformListAllWorkflowsResponse,
                        response
                    )
                    .ConfigureAwait(false);
                break;
            case "ListEntityRelationshipTypes":
                await this.lstEntRltTyp_TransformListEntityRelationshipTypes(response)
                    .ConfigureAwait(false);
                break;
            case "GetEntityRelationshipType":
                await this.getEntRltTyp_TransformResponse(response).ConfigureAwait(false);
                break;
            case "RetrieveEntity":
                await this.rtvEnt_TransformResponse(response).ConfigureAwait(false);
                break;
            case "ListAllEntities":
                await this.lstAllEnt_TransformResponse(response).ConfigureAwait(false);
                break;
            case "UpdateWorkflowMetadata":
                await this.updWflMd_TransformUpdateWorkflowMetadataResponse(response).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Reads a JSON response body, runs the supplied transformer, and writes the new JSON back.
    /// The helper only runs when the response body is non-empty to preserve current behavior.
    /// </summary>
    private async Task TransformResponseJsonBody(
        Func<JObject, JObject> transformationFunction,
        HttpResponseMessage response
    )
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!String.IsNullOrWhiteSpace(content))
        {
            var body = JObject.Parse(content);
            body = transformationFunction(body);
            response.Content = CreateJsonContent(body.ToString());
        }
    }

    /// <summary>
    /// Reads the current request body as JSON.
    /// The existing connector only calls this for operations that already expect JSON input.
    /// </summary>
    private async Task<JObject> ReadRequestBodyAsObjectAsync()
    {
        var content = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JObject.Parse(content);
    }

    /// <summary>
    /// Replaces the current request content with a JSON body created from the provided object.
    /// </summary>
    private void ReplaceRequestJsonBody(JObject jsonBody)
    {
        this.Context.Request.Content = CreateJsonContent(jsonBody.ToString());
    }

    /// <summary>
    /// Copies the current request headers to a follow-up internal request so downstream
    /// authentication and caller context remain unchanged.
    /// </summary>
    private void CopyCurrentRequestHeaders(HttpRequestMessage request)
    {
        foreach (var header in this.Context.Request.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    /// <summary>
    /// Fetches a JSON payload from the same Ironclad host used by the current request.
    /// This is used for metadata lookups that enrich connector-friendly request/response shapes.
    /// </summary>
    private async Task<JObject> FetchJsonFromConnectorApiAsync(
        string relativePath,
        string failureMessage
    )
    {
        var baseUrl = this.Context.Request.RequestUri.GetLeftPart(UriPartial.Authority);
        var requestUri = new Uri(new Uri(baseUrl), relativePath);
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        CopyCurrentRequestHeaders(request);

        var response = await this.Context.SendAsync(request, this.CancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"{failureMessage} Status code: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JObject.Parse(content);
    }

    /// <summary>
    /// Builds the multipart payload used by record and workflow-document attachment uploads.
    /// The payload format stays unchanged so the connector contract remains stable.
    /// </summary>
    private MultipartFormDataContent CreateAttachmentMultipartContent(JObject jsonBody)
    {
        var multipartContent = new MultipartFormDataContent();

        string filename = "document.pdf";
        if (
            jsonBody.TryGetValue("metadata", out var metadataToken)
            && metadataToken is JObject metadata
        )
        {
            filename = metadata["filename"]?.ToString() ?? filename;
        }

        if (jsonBody.TryGetValue("attachment", out var attachmentToken))
        {
            var attachmentBytes = Convert.FromBase64String(attachmentToken.ToString());
            var fileContent = new ByteArrayContent(attachmentBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipartContent.Add(fileContent, "attachment", filename);
        }

        if (metadataToken != null)
        {
            var metadataJson = metadataToken.ToString();
            var metadataContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");
            multipartContent.Add(metadataContent, "metadata");
        }

        return multipartContent;
    }

    /// <summary>
    /// Reads a query string value from the current request without changing any existing parameter names.
    /// </summary>
    private string GetRequestQueryValue(string parameterName)
    {
        return HttpUtility.ParseQueryString(this.Context.Request.RequestUri.Query)[parameterName]
            ?? string.Empty;
    }

    /// <summary>
    /// Parses an ISO-8601 duration into the connector's current expanded object shape.
    /// A delegate is used for the nested descriptions because different routes expose
    /// slightly different wording while keeping the same field structure.
    /// </summary>
    private JObject CreateExpandedDurationObject(string isoDuration)
    {
        var result = new JObject { ["isoDuration"] = isoDuration };

        var regex = new Regex(@"P(?:(\d+)Y)?(?:(\d+)M)?(?:(\d+)W)?(?:(\d+)D)?");
        var match = regex.Match(isoDuration);

        if (match.Success)
        {
            result["years"] = string.IsNullOrEmpty(match.Groups[1].Value)
                ? 0
                : int.Parse(match.Groups[1].Value);
            result["months"] = string.IsNullOrEmpty(match.Groups[2].Value)
                ? 0
                : int.Parse(match.Groups[2].Value);
            result["weeks"] = string.IsNullOrEmpty(match.Groups[3].Value)
                ? 0
                : int.Parse(match.Groups[3].Value);
            result["days"] = string.IsNullOrEmpty(match.Groups[4].Value)
                ? 0
                : int.Parse(match.Groups[4].Value);
        }

        return result;
    }

    /// <summary>
    /// Shared dispatcher for record property schemas. Callers provide the specialized
    /// builders for monetary amounts and durations because those nested descriptions vary
    /// slightly between routes, while the primitive fallback remains shared.
    /// </summary>
    private JObject FormatRecordPropertySchemaCore(
        string propertyType,
        string displayName,
        string description,
        Func<string, string, JObject> monetaryFormatter,
        Func<string, string, JObject> durationFormatter
    )
    {
        switch (propertyType)
        {
            case "monetary_amount":
                return monetaryFormatter(displayName, description);
            case "duration":
                return durationFormatter(displayName, description);
            default:
                return rtrRcd_FormatBasicPropertySchema(propertyType, displayName, description);
        }
    }

    /// <summary>
    /// Ensures SCIM patch requests always contain the required schema marker expected by Ironclad.
    /// </summary>
    private void EnsureScimPatchSchema(JObject jsonBody)
    {
        if (!jsonBody.ContainsKey("schemas") || !(jsonBody["schemas"] is JArray))
        {
            jsonBody["schemas"] = new JArray();
        }

        var schemas = (JArray)jsonBody["schemas"];
        if (
            !schemas.Any(
                s => s.Type == JTokenType.String && s.Value<string>() == ScimPatchOperationSchema
            )
        )
        {
            schemas.Clear();
            schemas.Add(ScimPatchOperationSchema);
        }
    }

    /// <summary>
    /// Normalizes entity type names so equivalent Ironclad variants map to the connector's
    /// current canonical names.
    /// </summary>
    private string NormalizeEntityTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return "string";
        }

        var normalizedType = typeName.ToLowerInvariant();
        if (normalizedType == "monetaryamount" || normalizedType == "monetary_amount")
        {
            return "monetary_amount";
        }

        return normalizedType;
    }

    /// <summary>
    /// Resolves the effective entity type name from either a relationship-type definition
    /// or an already-populated property object.
    /// </summary>
    private string GetEntityTypeName(JObject propertyDefinition, JObject propertyObject = null)
    {
        var rawType =
            propertyDefinition?.Value<string>("fullTypeName")
            ?? propertyDefinition?.SelectToken("type.typeName")?.ToString()
            ?? propertyDefinition?.Value<string>("type")
            ?? propertyObject?.Value<string>("fullTypeName")
            ?? propertyObject?.Value<string>("type")
            ?? "string";

        return NormalizeEntityTypeName(rawType);
    }

    /// <summary>
    /// Copies entity property definition metadata onto a property value object so downstream
    /// formatters can work from a consistent shape.
    /// </summary>
    private void ApplyEntityPropertyDefinition(
        JObject propertyObject,
        string propertyName,
        JObject propertyDefinition
    )
    {
        propertyObject["systemName"] = propertyName;
        propertyObject["displayName"] =
            propertyDefinition?.Value<string>("displayName") ?? propertyName;
        propertyObject["description"] =
            propertyDefinition?.Value<string>("description") ?? $"The {propertyName}.";
        propertyObject["type"] =
            propertyDefinition?.Value<string>("fullTypeName")
            ?? propertyDefinition?.SelectToken("type.typeName")?.ToString()
            ?? propertyObject.Value<string>("type")
            ?? "string";
        propertyObject["hidden"] = propertyDefinition?.Value<bool?>("hidden") ?? false;
        propertyObject["required"] = propertyDefinition?.Value<bool?>("required") ?? false;
    }

    /// <summary>
    /// Creates the compact relationship type object exposed by the connector for entities.
    /// </summary>
    private JObject CreateEntityRelationshipTypeSummary(JObject relationshipType)
    {
        return new JObject
        {
            ["id"] = relationshipType.Value<string>("id"),
            ["name"] = relationshipType.Value<string>("name"),
            ["displayName"] = relationshipType.Value<string>("displayName"),
            ["description"] = relationshipType.Value<string>("description") ?? ""
        };
    }

    /// <summary>
    /// Resolves relationship type IDs into the compact connector-specific objects used in
    /// entity responses.
    /// </summary>
    private JArray BuildEntityRelationshipTypesArray(
        JArray relationshipTypeIds,
        Dictionary<string, JObject> relationshipTypesById
    )
    {
        var relationshipTypes = new JArray();

        if (relationshipTypeIds == null)
        {
            return relationshipTypes;
        }

        foreach (var relationshipTypeIdToken in relationshipTypeIds)
        {
            var relationshipTypeId = relationshipTypeIdToken?.ToString()?.Trim().ToLowerInvariant();
            if (
                !string.IsNullOrEmpty(relationshipTypeId)
                && relationshipTypesById.TryGetValue(relationshipTypeId, out var relationshipType)
            )
            {
                relationshipTypes.Add(CreateEntityRelationshipTypeSummary(relationshipType));
            }
        }

        return relationshipTypes;
    }

    /// <summary>
    /// Ensures entity request payloads always send relationshipTypeKey as an array when the
    /// connector input supplied a single string value.
    /// </summary>
    private void EnsureEntityRelationshipTypeKeyArray(JObject jsonBody)
    {
        if (
            jsonBody.TryGetValue("relationshipTypeKey", out var relationshipTypeKeyToken)
            && relationshipTypeKeyToken.Type == JTokenType.String
        )
        {
            jsonBody["relationshipTypeKey"] = new JArray(relationshipTypeKeyToken.ToString());
        }
    }

    /// <summary>
    /// Converts the connector's expanded duration object back into the ISO-8601 string form
    /// expected by Ironclad for entity writes.
    /// </summary>
    private string ConvertExpandedDurationToIsoString(JObject durationObject)
    {
        var years = durationObject.Value<int?>("years") ?? 0;
        var months = durationObject.Value<int?>("months") ?? 0;
        var weeks = durationObject.Value<int?>("weeks") ?? 0;
        var days = durationObject.Value<int?>("days") ?? 0;

        if (years > 0 || months > 0 || weeks > 0 || days > 0)
        {
            var builder = new StringBuilder("P");
            if (years > 0)
            {
                builder.Append($"{years}Y");
            }

            if (months > 0)
            {
                builder.Append($"{months}M");
            }

            if (weeks > 0)
            {
                builder.Append($"{weeks}W");
            }

            if (days > 0)
            {
                builder.Append($"{days}D");
            }

            return builder.ToString();
        }

        return "P0D";
    }

    /// <summary>
    /// Normalizes entity request values before they are sent to Ironclad.
    /// </summary>
    private JToken NormalizeEntityRequestValue(string typeName, JToken valueToken)
    {
        if (
            NormalizeEntityTypeName(typeName) == "duration"
            && valueToken is JObject durationObject
        )
        {
            return ConvertExpandedDurationToIsoString(durationObject);
        }

        return valueToken;
    }

    /// <summary>
    /// Clones the current entity property objects into the array form exposed by the connector.
    /// </summary>
    private JArray CreateEntityPropertiesAsArray(JObject properties)
    {
        var propertiesArray = new JArray();

        if (properties == null)
        {
            return propertiesArray;
        }

        foreach (var property in properties.Properties())
        {
            if (property.Value is JObject propertyObject)
            {
                propertiesArray.Add(new JObject(propertyObject));
            }
        }

        return propertiesArray;
    }

    /// <summary>
    /// Builds the value schema used by GetEntityRelationshipType for a single entity property.
    /// This route exposes a connector-specific schema shape that differs from the entity
    /// response formatters, so the helper is kept route-specific.
    /// </summary>
    private JObject CreateEntityRelationshipTypeValueSchema(
        string type,
        string displayName,
        string description
    )
    {
        switch (type)
        {
            case "monetary_amount":
                return new JObject
                {
                    ["type"] = "object",
                    ["title"] = "Value",
                    ["description"] = description,
                    ["x-ms-visibility"] = "important",
                    ["properties"] = new JObject
                    {
                        ["amount"] = new JObject
                        {
                            ["type"] = "number",
                            ["title"] = "Amount",
                            ["description"] = $"The amount of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        },
                        ["currency"] = new JObject
                        {
                            ["type"] = "string",
                            ["title"] = "Currency",
                            ["description"] = $"The currency of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        }
                    },
                    ["required"] = new JArray { "amount", "currency" }
                };
            case "duration":
                return new JObject
                {
                    ["type"] = "object",
                    ["title"] = "Value",
                    ["description"] = description,
                    ["x-ms-visibility"] = "important",
                    ["properties"] = new JObject
                    {
                        ["years"] = new JObject
                        {
                            ["type"] = "number",
                            ["title"] = "Years",
                            ["description"] = $"The years of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        },
                        ["months"] = new JObject
                        {
                            ["type"] = "number",
                            ["title"] = "Months",
                            ["description"] = $"The months of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        },
                        ["weeks"] = new JObject
                        {
                            ["type"] = "number",
                            ["title"] = "Weeks",
                            ["description"] = $"The weeks of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        },
                        ["days"] = new JObject
                        {
                            ["type"] = "number",
                            ["title"] = "Days",
                            ["description"] = $"The days of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        }
                    }
                };
            case "address":
                return new JObject
                {
                    ["type"] = "object",
                    ["title"] = "Value",
                    ["description"] = description,
                    ["x-ms-visibility"] = "important",
                    ["properties"] = new JObject
                    {
                        ["lines"] = new JObject
                        {
                            ["type"] = "array",
                            ["title"] = "Address Lines",
                            ["description"] = $"The lines of the {displayName}.",
                            ["items"] = new JObject
                            {
                                ["type"] = "string",
                                ["title"] = $"{displayName} Line",
                                ["description"] = $"An individual line of the {displayName}.",
                                ["x-ms-visibility"] = "important"
                            },
                            ["x-ms-visibility"] = "important"
                        },
                        ["locality"] = new JObject
                        {
                            ["type"] = "string",
                            ["title"] = "Locality",
                            ["description"] = $"The locality of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        },
                        ["region"] = new JObject
                        {
                            ["type"] = "string",
                            ["title"] = "Region",
                            ["description"] = $"The region of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        },
                        ["postcode"] = new JObject
                        {
                            ["type"] = "string",
                            ["title"] = "Postcode",
                            ["description"] = $"The postcode of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        },
                        ["country"] = new JObject
                        {
                            ["type"] = "string",
                            ["title"] = "Country",
                            ["description"] = $"The country of the {displayName}.",
                            ["x-ms-visibility"] = "important"
                        }
                    }
                };
            case "boolean":
                return new JObject
                {
                    ["type"] = "boolean",
                    ["title"] = "Value",
                    ["description"] = description,
                    ["x-ms-visibility"] = "important"
                };
            case "number":
                return new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Value",
                    ["description"] = description,
                    ["x-ms-visibility"] = "important"
                };
            case "email":
                return new JObject
                {
                    ["type"] = "string",
                    ["format"] = "email",
                    ["title"] = "Value",
                    ["description"] = description,
                    ["x-ms-visibility"] = "important"
                };
            case "date":
                return new JObject
                {
                    ["type"] = "string",
                    ["format"] = "date-time",
                    ["title"] = "Value",
                    ["description"] = description,
                    ["x-ms-visibility"] = "important"
                };
            default:
                return new JObject
                {
                    ["type"] = "string",
                    ["title"] = "Value",
                    ["description"] = description,
                    ["x-ms-visibility"] = "important"
                };
        }
    }

    /// <summary>
    /// Wraps an entity relationship-type value schema in the outer property schema expected
    /// by GetEntityRelationshipType.
    /// </summary>
    private JObject CreateEntityRelationshipTypePropertySchema(
        string displayName,
        string description,
        bool hidden,
        string type,
        JObject valueSchema
    )
    {
        return new JObject
        {
            ["type"] = "object",
            ["title"] = displayName,
            ["description"] = description,
            ["x-ms-visibility"] = hidden ? "advanced" : "important",
            ["properties"] = new JObject
            {
                ["value"] = valueSchema,
                ["type"] = new JObject
                {
                    ["type"] = "string",
                    ["title"] = "Type",
                    ["description"] = "The Ironclad data type.",
                    ["x-ms-visibility"] = "internal",
                    ["default"] = type
                }
            },
            ["required"] = new JArray { "value", "type" }
        };
    }

    /// <summary>
    /// Builds the metadata-array item exposed by GetEntityRelationshipType.
    /// </summary>
    private JObject CreateEntityRelationshipTypePropertyArrayItem(
        string propertyName,
        string displayName,
        string description,
        bool hidden,
        bool required,
        string type
    )
    {
        return new JObject
        {
            ["key"] = propertyName,
            ["displayName"] = displayName,
            ["description"] = description,
            ["hidden"] = hidden,
            ["required"] = required,
            ["type"] = type
        };
    }

    // ################################################################################
    // Create Attachment / Create Signed Copy Attachment operations ###################
    // ################################################################################

    /// <summary>
    /// Converts the connector's JSON attachment payload into the multipart form expected
    /// by the Ironclad record attachment endpoint.
    /// </summary>
    private async Task crtAtt_TransformToMultipartRequestForRecords()
    {
        var jsonBody = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);
        this.Context.Request.Content = CreateAttachmentMultipartContent(jsonBody);
    }

    // ################################################################################
    // Create Record ##################################################################
    // Replace Record #################################################################
    // ################################################################################

    /// <summary>
    /// Retrieves record metadata from Ironclad so array-style connector inputs can be
    /// converted into the typed object structure expected by the API.
    /// </summary>
    private async Task<JObject> crtRcd_FetchRecordMetadata()
    {
        return await FetchJsonFromConnectorApiAsync(
                "/public/api/v1/records/metadata",
                "Failed to retrieve record metadata."
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Converts the connector's array-based record properties into Ironclad's keyed
    /// properties object while preserving the exact request contract already in use.
    /// </summary>
    private async Task crtRcd_TransformCreateRecordRequest()
    {
        var jsonBody = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);

        if (jsonBody.TryGetValue("propertiesAsArray", out var propertiesAsArray))
        {
            var metadata = await crtRcd_FetchRecordMetadata();
            var metadataProperties = metadata["properties"] as JObject;

            var properties = new JObject();

            foreach (var prop in propertiesAsArray)
            {
                var systemPropertyName = prop["propertySystemName"].ToString();
                var value = prop["value"];

                string type = "unknown";
                if (
                    metadataProperties != null
                    && metadataProperties.TryGetValue(systemPropertyName, out var propertyInfo)
                )
                {
                    type = propertyInfo["type"]?.ToString() ?? "unknown";
                }

                properties[systemPropertyName] = new JObject { ["value"] = value, ["type"] = type };
            }

            jsonBody["properties"] = properties;
            jsonBody.Remove("propertiesAsArray");

            ReplaceRequestJsonBody(jsonBody);
        }
    }

    /// <summary>
    /// Adds the root-level counterpartyName field expected by the connector after a
    /// record create or replace response.
    /// </summary>
    private JObject crtRcd_TransformCreateRecordResponse(JObject body)
    {
        if (body.ContainsKey("properties") && body["properties"] is JObject properties)
        {
            if (properties.ContainsKey("counterpartyName"))
            {
                body["counterpartyName"] = properties["counterpartyName"]["value"];
            }
            else
            {
                body["counterpartyName"] = null;
            }
        }
        else
        {
            body["counterpartyName"] = null;
        }

        return body;
    }

    // ################################################################################
    // Retrieve Entity ################################################################
    // ################################################################################

    /// <summary>
    /// Enriches a retrieved entity with formatted property metadata, relationship type
    /// details, and connector-specific helper outputs.
    /// </summary>
    private async Task rtvEnt_TransformResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            return;

        var obj = JObject.Parse(content);

        // Fetch all relationship type and property info (single call!)
        var (_, propDict, relTypeById) = await FetchEntityPropertyDefinitionsAndTypes();

        // ----------------- Relationship Types -----------------
        if (obj.TryGetValue("namedTypeIds", out var namedTypeIdsToken) && namedTypeIdsToken is JArray idArray)
        {
            var relTypesArray = BuildEntityRelationshipTypesArray(idArray, relTypeById);
            obj["relationshipTypes"] = relTypesArray;
            obj.Remove("namedTypeIds");
        }

        // ----------------- Properties Handling -----------------
        if (obj["properties"] is JObject props)
        {
            var formattedSchemaProperties = new JObject();
            var transformedProps = new JObject();

            foreach (var prop in props.Properties())
            {
                var propName = prop.Name;
                var propertyObj = prop.Value as JObject;
                if (propertyObj == null) continue;

                JObject propDef = null;
                if (propDict.TryGetValue(propName, out var dictValue))
                {
                    propDef = dictValue;
                }

                ApplyEntityPropertyDefinition(propertyObj, propName, propDef);

                var type = GetEntityTypeName(propDef, propertyObj);

                JObject propertySchema = null;
                JToken formattedValue = propertyObj["value"];

                switch (type)
                {
                    case "monetary_amount":
                        propertySchema = new JObject
                        {
                            ["type"] = "object",
                            ["title"] = propertyObj["displayName"],
                            ["description"] = propertyObj["description"],
                            ["x-ms-visibility"] = propertyObj.Value<bool?>("hidden") == true ? "advanced" : "important",
                            ["properties"] = new JObject
                            {
                                ["amount"] = new JObject
                                {
                                    ["type"] = "number",
                                    ["title"] = "Amount",
                                    ["description"] = $"The amount of the {propertyObj["displayName"]}.",
                                    ["x-ms-visibility"] = "important"
                                },
                                ["currency"] = new JObject
                                {
                                    ["type"] = "string",
                                    ["title"] = "Currency",
                                    ["description"] = $"The currency of the {propertyObj["displayName"]}.",
                                    ["x-ms-visibility"] = "important"
                                }
                            }
                        };
                        var val = propertyObj["value"] as JObject;
                        formattedValue = new JObject
                        {
                            ["amount"] = val?["amount"],
                            ["currency"] = val?["currency"]
                        };
                        break;

                    case "duration":
                        propertySchema = new JObject
                        {
                            ["type"] = "object",
                            ["title"] = propertyObj["displayName"],
                            ["description"] = propertyObj["description"],
                            ["x-ms-visibility"] = propertyObj.Value<bool?>("hidden") == true ? "advanced" : "important",
                            ["properties"] = new JObject
                            {
                                ["isoDuration"] = new JObject
                                {
                                    ["type"] = "string",
                                    ["title"] = "ISO Duration",
                                    ["description"] = $"The ISO 8601 duration representation of the {propertyObj["displayName"]}.",
                                    ["x-ms-visibility"] = "important"
                                },
                                ["years"] = new JObject
                                {
                                    ["type"] = "number",
                                    ["title"] = "Years",
                                    ["description"] = $"The years of the {propertyObj["displayName"]}.",
                                    ["x-ms-visibility"] = "important"
                                },
                                ["months"] = new JObject
                                {
                                    ["type"] = "number",
                                    ["title"] = "Months",
                                    ["description"] = $"The months of the {propertyObj["displayName"]}.",
                                    ["x-ms-visibility"] = "important"
                                },
                                ["weeks"] = new JObject
                                {
                                    ["type"] = "number",
                                    ["title"] = "Weeks",
                                    ["description"] = $"The weeks of the {propertyObj["displayName"]}.",
                                    ["x-ms-visibility"] = "important"
                                },
                                ["days"] = new JObject
                                {
                                    ["type"] = "number",
                                    ["title"] = "Days",
                                    ["description"] = $"The days of the {propertyObj["displayName"]}.",
                                    ["x-ms-visibility"] = "important"
                                }
                            }
                        };
                        var iso = propertyObj.Value<string>("value") ?? "";
                        formattedValue = CreateExpandedDurationObject(iso);
                        break;

                    case "address":
                        propertySchema = new JObject
                        {
                            ["type"] = "object",
                            ["title"] = propertyObj["displayName"],
                            ["description"] = propertyObj["description"],
                            ["x-ms-visibility"] = propertyObj.Value<bool?>("hidden") == true ? "advanced" : "important",
                            ["properties"] = new JObject
                            {
                                ["lines"] = new JObject
                                {
                                    ["type"] = "array",
                                    ["items"] = new JObject { ["type"] = "string" },
                                    ["title"] = "Address Lines",
                                    ["description"] = "The lines of the address.",
                                    ["x-ms-visibility"] = "important"
                                },
                                ["locality"] = new JObject { ["type"] = "string", ["title"] = "Locality", ["description"] = "The locality.", ["x-ms-visibility"] = "important" },
                                ["region"] = new JObject { ["type"] = "string", ["title"] = "Region", ["description"] = "The region.", ["x-ms-visibility"] = "important" },
                                ["postcode"] = new JObject { ["type"] = "string", ["title"] = "Postcode", ["description"] = "The postcode.", ["x-ms-visibility"] = "important" },
                                ["country"] = new JObject { ["type"] = "string", ["title"] = "Country", ["description"] = "The country.", ["x-ms-visibility"] = "important" }
                            }
                        };
                        formattedValue = propertyObj["value"];
                        break;

                    case "boolean":
                        propertySchema = new JObject
                        {
                            ["type"] = "boolean",
                            ["title"] = propertyObj["displayName"],
                            ["description"] = propertyObj["description"],
                            ["x-ms-visibility"] = propertyObj.Value<bool?>("hidden") == true ? "advanced" : "important"
                        };
                        formattedValue = propertyObj["value"];
                        break;

                    case "number":
                        propertySchema = new JObject
                        {
                            ["type"] = "number",
                            ["title"] = propertyObj["displayName"],
                            ["description"] = propertyObj["description"],
                            ["x-ms-visibility"] = propertyObj.Value<bool?>("hidden") == true ? "advanced" : "important"
                        };
                        formattedValue = propertyObj["value"];
                        break;

                    case "date":
                        propertySchema = new JObject
                        {
                            ["type"] = "string",
                            ["format"] = "date-time",
                            ["title"] = propertyObj["displayName"],
                            ["description"] = propertyObj["description"],
                            ["x-ms-visibility"] = propertyObj.Value<bool?>("hidden") == true ? "advanced" : "important"
                        };
                        formattedValue = propertyObj["value"];
                        break;

                    case "email":
                        propertySchema = new JObject
                        {
                            ["type"] = "string",
                            ["format"] = "email",
                            ["title"] = propertyObj["displayName"],
                            ["description"] = propertyObj["description"],
                            ["x-ms-visibility"] = propertyObj.Value<bool?>("hidden") == true ? "advanced" : "important"
                        };
                        formattedValue = propertyObj["value"];
                        break;

                    case "string":
                    default:
                        propertySchema = new JObject
                        {
                            ["type"] = "string",
                            ["title"] = propertyObj["displayName"],
                            ["description"] = propertyObj["description"],
                            ["x-ms-visibility"] = propertyObj.Value<bool?>("hidden") == true ? "advanced" : "important"
                        };
                        formattedValue = propertyObj["value"];
                        break;
                }

                formattedSchemaProperties[propName] = propertySchema;
                transformedProps[propName] = formattedValue;
            }

            // --- Build propertiesAsArray ---
            var propertiesAsArray = new JArray();
            foreach (var prop in props.Properties())
            {
                var propertyObj = prop.Value as JObject;
                if (propertyObj == null) continue;

                var arrObj = new JObject
                {
                    ["key"] = propertyObj.Value<string>("systemName") ?? prop.Name,
                    ["displayName"] = propertyObj.Value<string>("displayName") ?? prop.Name,
                    ["description"] = propertyObj.Value<string>("description") ?? $"The {prop.Name}.",
                    ["hidden"] = propertyObj.Value<bool?>("hidden") ?? false,
                    ["required"] = propertyObj.Value<bool?>("required") ?? false,
                    ["type"] = propertyObj.Value<string>("type") ?? "string",
                    ["value"] = transformedProps[prop.Name]
                };
                propertiesAsArray.Add(arrObj);
            }

            obj["propertiesAsArray"] = propertiesAsArray;
            obj["formattedSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = formattedSchemaProperties
            };
            obj["formattedProperties"] = transformedProps;
        }

        response.Content = CreateJsonContent(obj.ToString());
    }

    // ################################################################################
    // Create Entity ##################################################################
    // ################################################################################

    /// <summary>
    /// Rewrites connector-style entity create properties into the typed Ironclad request
    /// shape before the downstream API call is sent.
    /// </summary>
    private async Task crtEnt_TransformCreateEntityRequest()
    {
        var jsonBody = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);

        if (jsonBody.TryGetValue("properties", out var propertiesToken) && propertiesToken is JObject properties)
        {
            var reformatted = new JObject();

            foreach (var prop in properties.Properties())
            {
                var propName = prop.Name;
                var propObj = prop.Value as JObject;
                if (propObj == null)
                    continue;

                var type = propObj.Value<string>("type") ?? "";
                var valueToken = NormalizeEntityRequestValue(type, propObj["value"]);

                // Add to new properties object in Ironclad structure
                reformatted[propName] = new JObject
                {
                    ["type"] = type,
                    ["value"] = valueToken
                };
            }

            // Overwrite original properties
            jsonBody["properties"] = reformatted;
        }

        EnsureEntityRelationshipTypeKeyArray(jsonBody);

        ReplaceRequestJsonBody(jsonBody);
    }

    // ################################################################################
    // Update Entity ##################################################################
    // ################################################################################

    /// <summary>
    /// Rewrites connector-style entity update fields into the typed Ironclad payload
    /// expected for addProperties and relationship type updates.
    /// </summary>
    private async Task updEnt_TransformUpdateEntityRequest()
    {
        var jsonBody = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);

        EnsureEntityRelationshipTypeKeyArray(jsonBody);

        // Transform addProperties array to object keyed by property name with type/value
        if (jsonBody.TryGetValue("addProperties", out var addPropsToken) && addPropsToken is JArray addPropsArray)
        {
            var (_, propDict, _) = await FetchEntityPropertyDefinitionsAndTypes();
            var addPropsObj = new JObject();

            foreach (var item in addPropsArray.OfType<JObject>())
            {
                var key = item.Value<string>("key");
                var value = item["value"];
                if (string.IsNullOrEmpty(key))
                    continue;

                // Get and normalize type
                var type = propDict.TryGetValue(key, out var def)
                    ? GetEntityTypeName(def)
                    : "string";

                addPropsObj[key] = new JObject
                {
                    ["type"] = type,
                    ["value"] = value
                };
            }

            // Overwrite array with object
            jsonBody["addProperties"] = addPropsObj;
        }

        ReplaceRequestJsonBody(jsonBody);
    }

    // ################################################################################
    // List All Entities ##############################################################
    // ################################################################################

    /// <summary>
    /// Enriches each listed entity with property metadata, relationship type summaries,
    /// and connector-friendly labels.
    /// </summary>
    private async Task lstAllEnt_TransformResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            return;

        var obj = JObject.Parse(content);

        // Fetch property definitions and all relationship types (single call!)
        var (_, propDict, relTypeById) = await FetchEntityPropertyDefinitionsAndTypes();

        if (obj["list"] is JArray list)
        {
            foreach (var entity in list.OfType<JObject>())
            {
                // ----------------- Properties Handling -----------------
                if (entity["properties"] is JObject props)
                {
                    foreach (var prop in props.Properties())
                    {
                        if (prop.Value is JObject propertyObj && propDict.TryGetValue(prop.Name, out var propDef))
                        {
                            propertyObj["systemName"]   = prop.Name;
                            propertyObj["displayName"]  = propDef?.Value<string>("displayName")  ?? prop.Name;
                            propertyObj["description"]  = propDef?.Value<string>("description")  ?? "";
                            propertyObj["fullTypeName"] = propDef?.Value<string>("fullTypeName")
                                                        ?? propDef?.SelectToken("type.typeName")?.ToString()
                                                        ?? propertyObj.Value<string>("type") ?? "string";
                            propertyObj["hidden"]       = propDef?.Value<bool?>("hidden")   ?? false;
                            propertyObj["required"]     = propDef?.Value<bool?>("required") ?? false;
                        }
                    }

                    // Create enriched propertiesAsArray
                    entity["propertiesAsArray"] = CreateEntityPropertiesAsArray(props);
                }

                // ----------------- RelationshipTypes Handling -----------------
                entity["relationshipTypes"] = BuildEntityRelationshipTypesArray(
                    entity["namedTypeIds"] as JArray,
                    relTypeById
                );
                entity.Remove("namedTypeIds"); // Optional

                // ----------------- Label Handling -----------------
                var name = entity["name"]?.ToString() ?? string.Empty;
                var ironcladId = entity["ironcladId"]?.ToString() ?? string.Empty;
                entity["label"] = $"{name} ({ironcladId})";
            }
        }
        response.Content = CreateJsonContent(obj.ToString());
    }

    // ################################################################################
    // Relationship Helper Function ###################################################
    // ################################################################################

    private async Task<(JArray allRelTypes, Dictionary<string, JObject> propDict, Dictionary<string, JObject> relTypeById)> FetchEntityPropertyDefinitionsAndTypes()
    {
        var uri = this.Context.Request.RequestUri;
        var baseUrl = uri.GetLeftPart(UriPartial.Authority);
        var relTypeUrl = new Uri(new Uri(baseUrl), "/public/api/v1/entities/relationship-types");
        var req = new HttpRequestMessage(HttpMethod.Get, relTypeUrl);

        foreach (var header in this.Context.Request.Headers)
            req.Headers.TryAddWithoutValidation(header.Key, header.Value);

        var resp = await this.Context.SendAsync(req, this.CancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Failed to fetch property definitions. Status: {resp.StatusCode}");

        var respContent = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        var arr = JArray.Parse(respContent);

        var propDict = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        var relTypeById = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        foreach (var relType in arr.OfType<JObject>())
        {
            var id = relType.Value<string>("id")?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(id))
                relTypeById[id] = relType;

            if (relType["properties"] is JObject props)
            {
                foreach (var prop in props.Properties())
                {
                    if (prop.Value is JObject propDef && !propDict.ContainsKey(prop.Name))
                    {
                        propDict[prop.Name] = propDef;
                    }
                }
            }
        }
        return (arr, propDict, relTypeById);
    }

    // ################################################################################
    // Relationship Types #############################################################
    // ################################################################################

    /// <summary>
    /// Expands the relationship type list response with merged property metadata and the
    /// connector's relationship type summary objects.
    /// </summary>
    private async Task lstEntRltTyp_TransformListEntityRelationshipTypes(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            return;

        var arr = JArray.Parse(content);

        // Collect all unique properties by 'key'
        var uniqueProps = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        foreach (var relTypeObj in arr.OfType<JObject>())
        {
            if (!(relTypeObj["properties"] is JObject props))
                continue;

            foreach (var prop in props.Properties())
            {
                var propObj = prop.Value as JObject;
                if (propObj == null)
                    continue;

                var key = propObj.Value<string>("key") ?? prop.Name;
                if (uniqueProps.ContainsKey(key))
                    continue;

                string description = propObj.Value<string>("description") ?? "";
                string displayName = propObj.Value<string>("displayName") ?? key;
                string fullTypeName = GetEntityTypeName(propObj);

                var propertyObj = new JObject
                {
                    ["key"] = key,
                    ["description"] = description,
                    ["displayName"] = displayName,
                    ["type"] = fullTypeName
                };
                uniqueProps[key] = propertyObj;
            }
        }

        // Compose the final result object
        var result = new JObject
        {
            ["relationshipTypes"] = arr,
            ["properties"] = new JArray(uniqueProps.Values)
        };

        response.Content = CreateJsonContent(result.ToString());
    } 

    // ################################################################################
    // Get Entity Relationship Type ###################################################
    // ################################################################################

    /// <summary>
    /// Rewrites the single relationship-type request into a list request and stores the
    /// requested system name so the response can be filtered afterward.
    /// </summary>
    private Task getEntRltTyp_TransformRequest()
    {
        // Example incoming path:
        //   /public/api/v1/entities/relationship-types/{systemName}
        var uri = this.Context.Request.RequestUri;
        var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var systemName = segments.Last(); // {systemName}

        // Stash the target system name in a transient header for later use.
        this.Context.Request.Headers.Remove("X-RelType-SystemName");
        this.Context.Request.Headers.Add("X-RelType-SystemName", systemName);

        // Rebuild the URI without the final segment so we hit the *list* endpoint.
        var listPath = "/" + string.Join("/", segments.Take(segments.Length - 1));
        var newUri = new Uri(uri.GetLeftPart(UriPartial.Authority) + listPath);
        this.Context.Request.RequestUri = newUri;

        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Filters the relationship-type list response down to the requested item and adds
    /// the formatted schema outputs exposed by the connector.
    /// </summary>
    private async Task getEntRltTyp_TransformResponse(HttpResponseMessage response)
    {
        if (!this.Context.Request.Headers.TryGetValues("X-RelType-SystemName", out var vals))
            return;

        var systemName = vals.FirstOrDefault();
        if (String.IsNullOrEmpty(systemName))
            return;

        var bodyText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(bodyText))
            return;

        var singleItem = JArray
            .Parse(bodyText)
            .OfType<JObject>()
            .FirstOrDefault(
                relTypeObj =>
                    (
                        relTypeObj["name"]?
                            .ToString()
                            .Equals(systemName, StringComparison.OrdinalIgnoreCase)
                    ).GetValueOrDefault()
            );

        if (singleItem != null)
        {
            getEntRltTyp_AddFormattedPropertyOutputs(singleItem);
        }

        response.Content = CreateJsonContent((singleItem ?? new JObject()).ToString());
    }

    /// <summary>
    /// Builds the connector-specific formattedSchema and propertiesAsArray values for a
    /// single entity relationship type while preserving the legacy field names and
    /// wording already exposed by the connector.
    /// </summary>
    private void getEntRltTyp_AddFormattedPropertyOutputs(JObject relTypeObj)
    {
        if (!(relTypeObj["properties"] is JObject props))
        {
            return;
        }

        var formattedSchemaProps = new JObject();
        var propertiesAsArray = new JArray();
        var requiredFields = new List<string>();

        foreach (var prop in props.Properties())
        {
            var propName = prop.Name;
            var propObj = prop.Value as JObject;
            if (propObj == null)
            {
                continue;
            }

            var type = GetEntityTypeName(propObj);
            var displayName = propObj.Value<string>("displayName") ?? propName;
            var description = propObj.Value<string>("description");
            if (string.IsNullOrWhiteSpace(description))
            {
                description = $"The {displayName}.";
            }

            var hidden = propObj.Value<bool?>("hidden") ?? false;
            var required = propObj.Value<bool?>("required") ?? false;
            if (required)
            {
                requiredFields.Add(propName);
            }

            var valueSchema = CreateEntityRelationshipTypeValueSchema(
                type,
                displayName,
                description
            );

            formattedSchemaProps[propName] = CreateEntityRelationshipTypePropertySchema(
                displayName,
                description,
                hidden,
                type,
                valueSchema
            );

            propertiesAsArray.Add(
                CreateEntityRelationshipTypePropertyArrayItem(
                    propName,
                    displayName,
                    description,
                    hidden,
                    required,
                    type
                )
            );
        }

        var formattedSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = formattedSchemaProps
        };

        if (requiredFields.Count > 0)
        {
            formattedSchema["required"] = new JArray(requiredFields);
        }

        relTypeObj["propertiesAsArray"] = propertiesAsArray;
        relTypeObj["formattedSchema"] = formattedSchema;
    }

    // ################################################################################
    // Update Record Metadata #########################################################
    // ################################################################################

    /// <summary>
    /// Retrieves record metadata for update operations so array-style property updates can
    /// be converted back into the typed API payload that Ironclad expects.
    /// </summary>
    private async Task<JObject> updRcd_FetchRecordMetadata()
    {
        return await FetchJsonFromConnectorApiAsync(
                "/public/api/v1/records/metadata",
                "Failed to retrieve record metadata."
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Converts array-based record metadata updates into the typed object form required by
    /// the Ironclad update endpoint.
    /// </summary>
    private async Task updRcd_TransformUpdateRecordMetadataRequest()
    {
        var metadata = await updRcd_FetchRecordMetadata();
        var metadataProperties = metadata["properties"] as JObject;

        var jsonBody = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);

        if (
            jsonBody.TryGetValue("addProperties", out var addPropertiesToken)
            && addPropertiesToken is JArray addPropertiesArray
        )
        {
            var transformedProperties = new JObject();

            foreach (var prop in addPropertiesArray)
            {
                if (
                    prop is JObject propObject
                    && propObject.TryGetValue("propertySystemName", out var propertySystemNameToken)
                    && propObject.TryGetValue("value", out var valueToken)
                )
                {
                    string propertySystemName = propertySystemNameToken.ToString();
                    JToken value = valueToken;

                    string type = "unknown";
                    if (
                        metadataProperties != null
                        && metadataProperties.TryGetValue(propertySystemName, out var propertyInfo)
                    )
                    {
                        type = propertyInfo["type"]?.ToString() ?? "unknown";
                    }

                    transformedProperties[propertySystemName] = new JObject
                    {
                        ["value"] = value,
                        ["type"] = type
                    };
                }
            }

            jsonBody["addProperties"] = transformedProperties;
            ReplaceRequestJsonBody(jsonBody);
        }
    }

    // ################################################################################
    // Create Workflow Asynchronously #################################################
    // ################################################################################

    /// <summary>
    /// Reshapes the async workflow create request from flattened connector inputs into
    /// the nested JSON structure expected by Ironclad.
    /// </summary>
    private async Task crtAsyncWfl_TransformRequestJson()
    {
        var content = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var jsonBody = JObject.Parse(content);

        // Unflatten the JSON structure
        jsonBody = crtAsyncWfl_UnflattenJson(jsonBody);

        // Transform launchApprovals into attributes using the new function
        jsonBody = crtAsyncWfl_ProcessLaunchApprovals(jsonBody);

        // Update the request content with transformed JSON
        this.Context.Request.Content = CreateJsonContent(jsonBody.ToString());
    }

    /// <summary>
    /// Converts launchApprovals array entries into direct attribute assignments for the
    /// async workflow create payload.
    /// </summary>
    private JObject crtAsyncWfl_ProcessLaunchApprovals(JObject json)
    {
        // Check if we have "attributes" at the root
        if (json.TryGetValue("attributes", out var attributesToken) && attributesToken is JObject attributes)
        {
            // Check if we have a launchApprovals array
            if (
                attributes.TryGetValue("launchApprovals", out var launchApprovalsToken)
                && launchApprovalsToken is JArray launchApprovalsArray
            )
            {
                // For each object in launchApprovals,
                // set the roleName as the key and the assignee as the value in attributes
                foreach (var item in launchApprovalsArray)
                {
                    if (item is JObject approvalObj)
                    {
                        var roleName = approvalObj["roleName"]?.ToString();
                        var assignee = approvalObj["assignee"]?.ToString();

                        // Only add if roleName is present
                        if (!string.IsNullOrEmpty(roleName))
                        {
                            attributes[roleName] = assignee ?? string.Empty;
                        }
                    }
                }

                // Finally, remove the entire launchApprovals array
                attributes.Remove("launchApprovals");
            }
        }

        return json;
    }

    /// <summary>
    /// Rebuilds nested objects and array items from the flattened field paths emitted by
    /// the connector designer for async workflow creation.
    /// </summary>
    private JObject crtAsyncWfl_UnflattenJson(JObject flatJson)
    {
        var result = new JObject();

        foreach (var prop in flatJson.Properties())
        {
            if (prop.Value is JArray array)
            {
                var unflattened = new JArray();
                foreach (var item in array)
                {
                    if (item is JObject objItem)
                    {
                        var unflattenedItem = new JObject();
                        var groupedProperties = objItem
                            .Properties()
                            .GroupBy(p => p.Name.Split('/')[0])
                            .ToDictionary(g => g.Key, g => g.ToList());

                        foreach (var group in groupedProperties)
                        {
                            if (group.Value.Count == 1 && !group.Value[0].Name.Contains("/"))
                            {
                                // Simple property
                                unflattenedItem[group.Key] = group.Value[0].Value;
                            }
                            else
                            {
                                // Complex property
                                var complexObj = new JObject();
                                foreach (var complexProp in group.Value)
                                {
                                    var parts = complexProp.Name.Split('/');
                                    if (parts.Length == 1)
                                    {
                                        complexObj[parts[0]] = complexProp.Value;
                                    }
                                    else
                                    {
                                        var currentObj = complexObj;
                                        for (int i = 1; i < parts.Length - 1; i++)
                                        {
                                            if (!currentObj.ContainsKey(parts[i]))
                                            {
                                                currentObj[parts[i]] = new JObject();
                                            }
                                            currentObj = (JObject)currentObj[parts[i]];
                                        }
                                        currentObj[parts.Last()] = complexProp.Value;
                                    }
                                }
                                unflattenedItem[group.Key] = complexObj;
                            }
                        }
                        unflattened.Add(unflattenedItem);
                    }
                    else
                    {
                        // Handle primitive types (e.g., strings, numbers) by adding them directly
                        unflattened.Add(item);
                    }
                }
                result[prop.Name] = unflattened;
            }
            else if (prop.Value is JObject obj)
            {
                result[prop.Name] = crtWfl_UnflattenJson(obj);
            }
            else if (prop.Name.Contains("/"))
            {
                var parts = prop.Name.Split('/');
                var currentObj = result;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!currentObj.ContainsKey(parts[i]))
                    {
                        currentObj[parts[i]] = new JObject();
                    }
                    currentObj = (JObject)currentObj[parts[i]];
                }
                currentObj[parts.Last()] = prop.Value;
            }
            else
            {
                result[prop.Name] = prop.Value;
            }
        }

        return result;
    }

    // ################################################################################
    // Create Workflow Synchronously ##################################################
    // ################################################################################

    // Transform error message on missing param
    /// <summary>
    /// Rewrites the specific CreateWorkflow approver validation error into the connector's
    /// more actionable response shape while leaving other errors unchanged.
    /// </summary>
    private async Task<HttpResponseMessage> TransformCreateWorkflowErrorResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var json = JObject.Parse(content);

                // Check if it's the "MISSING_PARAM" scenario
                if (json["code"]?.ToString() == "MISSING_PARAM" && json["param"] != null)
                {
                    // "param" is a string that looks like '["approverdfa77..."]', so parse it
                    string paramValue = json["param"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(paramValue))
                    {
                        // safely parse the stringified array
                        var paramArray = JArray.Parse(paramValue);

                        // Look for any item that starts with "approver"
                        var approverItems = paramArray
                            .Where(token => token.Type == JTokenType.String)
                            .Select(token => token.ToString())
                            .Where(str => str.StartsWith("approver", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (approverItems.Any())
                        {
                            // For simplicity, just transform the message for the first approver role
                            var firstApprover = approverItems.First();
                            json["message"] =
                                $"Missing assignment for approval role: \"{firstApprover}\". "
                                + "Please add it to the Launch Approvals in the launch action using the mentioned role.";

                            // Repack the updated JSON as the response content
                            response.Content = CreateJsonContent(json.ToString());
                        }
                    }
                }
            }
            catch
            {
                // If parsing fails, just return original response
            }
        }
        return response;
    }
    /// <summary>
    /// Converts the workflow create payload into multipart form data, including nested
    /// attribute reshaping and embedded file extraction.
    /// </summary>
    private async Task crtWfl_TransformToMultipartRequestForWorkflows()
    {
        var content = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var jsonBody = JObject.Parse(content);

        // Unflatten the entire JSON body
        jsonBody = crtWfl_UnflattenJson(jsonBody);

        jsonBody = crtWfl_ProcessLaunchApprovals(jsonBody);

        var multipartContent = new MultipartFormDataContent();

        if (
            jsonBody.TryGetValue("attributes", out var attributesToken)
            && attributesToken is JObject attributes
        )
        {
            crtWfl_ProcessWorkflowAttributes(attributes, multipartContent, jsonBody);
        }

        var dataContent = new StringContent(jsonBody.ToString(), Encoding.UTF8, "application/json");
        multipartContent.Add(dataContent, "data");

        this.Context.Request.Content = multipartContent;
    }

    /// <summary>
    /// Converts launchApprovals array entries into direct workflow attribute assignments
    /// for the synchronous workflow create request.
    /// </summary>
    private JObject crtWfl_ProcessLaunchApprovals(JObject json)
    {
        // Check if we have "attributes" at the root
        if (json.TryGetValue("attributes", out var attributesToken) && attributesToken is JObject attributes)
        {
            // Check if we have a launchApprovals array
            if (
                attributes.TryGetValue("launchApprovals", out var launchApprovalsToken)
                && launchApprovalsToken is JArray launchApprovalsArray
            )
            {
                // For each object in launchApprovals,
                // set the roleName as the key and the assignee as the value in attributes
                foreach (var item in launchApprovalsArray)
                {
                    if (item is JObject approvalObj)
                    {
                        var roleName = approvalObj["roleName"]?.ToString();
                        var assignee = approvalObj["assignee"]?.ToString();

                        // Only add if roleName is present
                        if (!string.IsNullOrEmpty(roleName))
                        {
                            attributes[roleName] = assignee ?? string.Empty;
                        }
                    }
                }

                // Finally, remove the entire launchApprovals array
                attributes.Remove("launchApprovals");
            }
        }

        return json;
    }
    /// <summary>
    /// Rebuilds nested workflow JSON objects and array items from slash-delimited field
    /// paths before the multipart request is assembled.
    /// </summary>
    private JObject crtWfl_UnflattenJson(JObject flatJson)
    {
        var result = new JObject();

        foreach (var prop in flatJson.Properties())
        {
            if (prop.Value is JArray array)
            {
                var unflattened = new JArray();
                foreach (var item in array)
                {
                    if (item is JObject objItem)
                    {
                        unflattened.Add(crtWfl_UnflattenArrayItemObject(objItem));
                    }
                    else
                    {
                        // Handle primitive types (e.g., strings, numbers) by adding them directly
                        unflattened.Add(item);
                    }
                }
                result[prop.Name] = unflattened;
            }
            else if (prop.Value is JObject obj)
            {
                result[prop.Name] = crtWfl_UnflattenJson(obj);
            }
            else if (prop.Name.Contains("/"))
            {
                crtWfl_AddNestedProperty(result, prop.Name, prop.Value, 0);
            }
            else
            {
                result[prop.Name] = prop.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Array items can arrive in a flat slash-delimited form as well. This helper keeps
    /// the current grouping semantics but avoids the previous LINQ-heavy grouping and
    /// dictionary materialization on every array element.
    /// </summary>
    private JObject crtWfl_UnflattenArrayItemObject(JObject objItem)
    {
        var groupedProperties = new Dictionary<string, List<JProperty>>();

        foreach (var property in objItem.Properties())
        {
            var slashIndex = property.Name.IndexOf('/');
            var groupKey =
                slashIndex >= 0 ? property.Name.Substring(0, slashIndex) : property.Name;

            if (!groupedProperties.TryGetValue(groupKey, out var properties))
            {
                properties = new List<JProperty>();
                groupedProperties[groupKey] = properties;
            }

            properties.Add(property);
        }

        var unflattenedItem = new JObject();

        foreach (var group in groupedProperties)
        {
            if (group.Value.Count == 1 && group.Value[0].Name.IndexOf('/') < 0)
            {
                unflattenedItem[group.Key] = group.Value[0].Value;
                continue;
            }

            var complexObj = new JObject();
            foreach (var complexProp in group.Value)
            {
                var slashIndex = complexProp.Name.IndexOf('/');
                if (slashIndex < 0)
                {
                    complexObj[complexProp.Name] = complexProp.Value;
                    continue;
                }

                crtWfl_AddNestedProperty(
                    complexObj,
                    complexProp.Name,
                    complexProp.Value,
                    slashIndex + 1
                );
            }

            unflattenedItem[group.Key] = complexObj;
        }

        return unflattenedItem;
    }

    /// <summary>
    /// Inserts a slash-delimited property path into the target JSON object starting at
    /// the requested index. The workflow request shaper uses this for both top-level
    /// properties and array items so the existing connector output stays identical while
    /// we avoid repeated split/group allocations.
    /// </summary>
    private void crtWfl_AddNestedProperty(
        JObject target,
        string propertyPath,
        JToken value,
        int startIndex
    )
    {
        var parts = propertyPath.Split('/');
        var currentObj = target;

        for (int i = startIndex; i < parts.Length - 1; i++)
        {
            if (!(currentObj[parts[i]] is JObject childObject))
            {
                childObject = new JObject();
                currentObj[parts[i]] = childObject;
            }

            currentObj = childObject;
        }

        currentObj[parts[parts.Length - 1]] = value;
    }

    /// <summary>
    /// Scans workflow attributes for file arrays and moves their binary content into the
    /// multipart payload while updating the JSON body to reference the generated keys.
    /// </summary>
    private void crtWfl_ProcessWorkflowAttributes(
        JObject attributes,
        MultipartFormDataContent multipartContent,
        JObject jsonBody
    )
    {
        foreach (var attribute in attributes.Properties())
        {
            if (
                attribute.Value is JArray arrayValue
                && arrayValue.Any(item => item is JObject obj && obj.ContainsKey("fileContent"))
            )
            {
                crtWfl_ProcessFileArrayAttribute(
                    attribute.Name,
                    arrayValue,
                    multipartContent,
                    jsonBody
                );
            }
        }
    }

    /// <summary>
    /// Extracts one workflow file-array attribute into multipart file parts and replaces
    /// each item with the file token reference expected by Ironclad.
    /// </summary>
    private void crtWfl_ProcessFileArrayAttribute(
        string attributeName,
        JArray arrayValue,
        MultipartFormDataContent multipartContent,
        JObject jsonBody
    )
    {
        var updatedArray = new JArray();

        foreach (var item in arrayValue)
        {
            if (
                item is JObject fileObject
                && fileObject.TryGetValue("fileName", out var fileNameToken)
                && fileObject.TryGetValue("fileContent", out var fileContentToken)
            )
            {
                string fileName = fileNameToken.ToString();
                string fileContent = fileContentToken.ToString();

                if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(fileContent))
                {
                    string fileKey = $"{attributeName}_{Guid.NewGuid().ToString("N")}";

                    var fileBytes = Convert.FromBase64String(fileContent);
                    var fileContentPart = new ByteArrayContent(fileBytes);
                    fileContentPart.Headers.ContentType = new MediaTypeHeaderValue(
                        "application/octet-stream"
                    );
                    multipartContent.Add(fileContentPart, fileKey, fileName);

                    updatedArray.Add(new JObject { ["file"] = fileKey });
                }
            }
        }

        jsonBody["attributes"][attributeName] = updatedArray;
    }

    // ################################################################################
    // Create Workflow Document operations ############################################
    // ################################################################################

    /// <summary>
    /// Converts the connector's JSON attachment payload into the multipart form expected
    /// by the Ironclad workflow-document upload endpoint.
    /// </summary>
    private async Task crtWflDoc_TransformToMultipartRequest()
    {
        var jsonBody = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);
        this.Context.Request.Content = CreateAttachmentMultipartContent(jsonBody);
    }

    // ################################################################################
    // Update Workflow Metadata #######################################################
    // ################################################################################

    /// <summary>
    /// Converts the connector's flattened workflow metadata update values back into JSON
    /// primitives, arrays, or objects when the payload clearly represents them.
    /// </summary>
    private async Task updWflMd_TransformUpdateWorkflowMetadataRequest()
    {
        var jsonBody = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);

        // Convert value fields to proper types based on their content
        await ConvertValueTypes(jsonBody).ConfigureAwait(false);
        
        ReplaceRequestJsonBody(jsonBody);
    }

    /// <summary>
    /// Converts the free-form "updates[*].value" strings used by the connector designer
    /// into richer JSON token types when the text clearly represents them.
    /// </summary>
    private async Task ConvertValueTypes(JObject jsonBody)
    {
        try
        {
            // Check if we have workflow attribute updates
            var updates = jsonBody["updates"] as JArray;
            if (updates != null)
            {
                // Process each update
                foreach (var update in updates)
                {
                    var valueToken = update["value"];
                    if (valueToken != null)
                    {
                        // Convert the value to the appropriate type
                        update["value"] = ConvertStringToProperType(valueToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail the request
            System.Diagnostics.Debug.WriteLine($"Error converting value types: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies the connector's current string-to-token conversion rules while keeping
    /// ambiguous values as strings.
    /// </summary>
    private JToken ConvertStringToProperType(JToken valueToken)
    {
        // If it's not a string, return as-is
        if (valueToken.Type != JTokenType.String)
            return valueToken;

        var stringValue = valueToken.ToString();
        
        // If empty or null, return as-is
        if (string.IsNullOrEmpty(stringValue))
            return valueToken;

        // Check if it's a quoted number (should remain as string)
        // Pattern: starts and ends with quotes and contains only digits/decimal
        if (stringValue.StartsWith("\"") && stringValue.EndsWith("\""))
        {
            var innerValue = stringValue.Substring(1, stringValue.Length - 2);
            if (IsNumeric(innerValue))
            {
                // This is a quoted number, keep as string but remove the extra quotes
                return JToken.FromObject(innerValue);
            }
        }

        // Try to parse as JSON array
        if (stringValue.StartsWith("[") && stringValue.EndsWith("]"))
        {
            try
            {
                return JArray.Parse(stringValue);
            }
            catch
            {
                // If parsing fails, keep as string
                return valueToken;
            }
        }

        // Try to parse as JSON object
        if (stringValue.StartsWith("{") && stringValue.EndsWith("}"))
        {
            try
            {
                return JObject.Parse(stringValue);
            }
            catch
            {
                // If parsing fails, keep as string
                return valueToken;
            }
        }

        // Try to parse as number (integer)
        if (int.TryParse(stringValue, out int intValue))
        {
            return JToken.FromObject(intValue);
        }

        // Try to parse as decimal/float
        if (decimal.TryParse(stringValue, out decimal decimalValue))
        {
            return JToken.FromObject(decimalValue);
        }

        // Try to parse as boolean
        if (bool.TryParse(stringValue, out bool boolValue))
        {
            return JToken.FromObject(boolValue);
        }

        // Keep as string for everything else
        return valueToken;
    }

    /// <summary>
    /// Checks whether a string can be parsed as a numeric value for workflow metadata
    /// request normalization.
    /// </summary>
    private bool IsNumeric(string value)
    {
        return decimal.TryParse(value, out _);
    }

    /// <summary>
    /// Normalizes the workflow metadata update response back into JSON content without
    /// changing the current payload shape.
    /// </summary>
    private async Task updWflMd_TransformUpdateWorkflowMetadataResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        
        if (!String.IsNullOrWhiteSpace(content))
        {
            var body = JObject.Parse(content);
            response.Content = CreateJsonContent(body.ToString());
        }
    }

    // ################################################################################
    // List Users #####################################################################
    // ################################################################################

    /// <summary>
    /// Trims the SCIM user list down to the fields the connector uses for dynamic values.
    /// </summary>
    private JObject lstUsr_TransformUsersList(JObject body)
    {
        var resources = body["Resources"] as JArray;
        if (resources != null)
        {
            var transformedResources = new JArray(
                resources.Select(
                    user =>
                    {
                        var givenName = user["name"]?["givenName"]?.ToString() ?? "";
                        var familyName = user["name"]?["familyName"]?.ToString() ?? "";
                        var displayName = $"{givenName} {familyName}".Trim();

                        var email =
                            user["emails"]?.FirstOrDefault()?["value"]?.ToString().ToLower() ?? "";

                        user["displayName"] = displayName;
                        user["combinedLabel"] = $"{displayName} ({email})";

                        return user;
                    }
                )
            );

            body["Resources"] = transformedResources;
        }
        return body;
    }

    // ################################################################################
    // Update Group ###################################################################
    // ################################################################################

    /// <summary>
    /// Ensures group patch requests always contain the SCIM patch schema required by Ironclad.
    /// </summary>
    private async Task updGrp_TransformUpdateGroupRequest()
    {
        var jsonBody = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);
        EnsureScimPatchSchema(jsonBody);
        ReplaceRequestJsonBody(jsonBody);
    }

    // ################################################################################
    // Update User ####################################################################
    // ################################################################################

    /// <summary>
    /// Ensures user patch requests always contain the SCIM patch schema required by Ironclad.
    /// </summary>
    private async Task updUsr_TransformUpdateUserRequest()
    {
        var jsonBody = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);
        EnsureScimPatchSchema(jsonBody);
        ReplaceRequestJsonBody(jsonBody);
    }

    // ################################################################################
    // Retrieve Workflow Schema #######################################################
    // ################################################################################

    /// <summary>
    /// Simplifies the workflow schema list response to the compact list shape used by the
    /// connector.
    /// </summary>
    private JObject lstWflSch_TransformListWorkflowSchemas(JObject body)
    {
        if (body["list"] is JArray list)
        {
            var trimmedList = new JArray();

            foreach (var item in list.OfType<JObject>())
            {
                trimmedList.Add(
                    new JObject
                    {
                        ["id"] = item["id"],
                        ["name"] = item["name"]
                    }
                );
            }

            body["list"] = trimmedList;
        }

        return body;
    }

    /// <summary>
    /// Builds the connector-specific launch schema, formatted schema, and schema arrays
    /// from the raw workflow schema response.
    /// </summary>
    private JObject rtrWflSch_TransformRetrieveWorkflowSchema(JObject body)
    {
        var schema = body["schema"] as JObject;
        if (schema != null)
        {
            var launchSchema = new JObject();
            var formattedSchema = new JObject();
            var schemaAsArray = new JArray();
            var documentSchemaAsArray = new JArray();
            var requiredProperties = new JArray();

            foreach (var property in schema.Properties())
            {
                string propertyName = property.Name;
                var propertyValue = property.Value as JObject;
                if (propertyValue != null)
                {
                    string displayName = propertyValue["displayName"]?.ToString() ?? propertyName;
                    string propertyType = propertyValue["type"]?.ToString().ToLower();
                    bool isReadOnly = propertyValue["readOnly"]?.ToObject<bool>() ?? false;

                    // Fix: Use the correct property type for special cases
                    string effectivePropertyType = propertyType;
                    if (propertyType == "object")
                    {
                        var objectType = propertyValue["objectType"]?.ToString().ToLower();
                        if (
                            objectType == "address"
                            || objectType == "monetaryamount"
                            || objectType == "duration"
                        )
                        {
                            effectivePropertyType = objectType;
                        }
                    }


                // Handle options, default, and required for launchSchema, formattedSchema, and schemaAsArray
                JObject formattedLaunchProperty = rtrWflSch_FormatLaunchProperty(
                    effectivePropertyType,
                    displayName,
                    propertyName,
                    propertyValue
                );
                // If options are present, add enum to launchSchema property
                if (propertyValue["options"] is JObject optionsObj && optionsObj["values"] is JArray valuesArr)
                {
                    // For multi-select (array type), enum must go on items, not the array itself.
                    // Putting enum on an array-typed property causes Power Automate to reject multiple values.
                    if (formattedLaunchProperty["type"]?.ToString() == "array"
                        && formattedLaunchProperty["items"] is JObject itemsObj)
                    {
                        itemsObj["enum"] = valuesArr.DeepClone();
                    }
                    else
                    {
                        formattedLaunchProperty["enum"] = valuesArr.DeepClone();
                    }
                }
                // If default is present, add to launchSchema property
                if (propertyValue["default"] != null)
                {
                    formattedLaunchProperty["default"] = propertyValue["default"].DeepClone();
                }
                // If required is 'always', add propertyName to requiredProperties
                if (propertyValue["required"] != null && propertyValue["required"].ToString() == "always")
                {
                    requiredProperties.Add(propertyName);
                }
                launchSchema[propertyName] = formattedLaunchProperty;

                // For formattedSchema, add options and default as-is if present
                JObject formattedProperty = rtrWflSch_FormatProperty(
                    effectivePropertyType,
                    displayName,
                    propertyName,
                    propertyValue
                );
                if (propertyValue["options"] != null)
                {
                    formattedProperty["options"] = propertyValue["options"].DeepClone();
                }
                if (propertyValue["default"] != null)
                {
                    formattedProperty["default"] = propertyValue["default"].DeepClone();
                }
                formattedSchema[propertyName] = formattedProperty;

                // For schemaAsArray, add options and default as-is if present
                JObject schemaArrayItem = rtrWflSch_ParseSchemaArrayItem(
                    propertyName,
                    displayName,
                    effectivePropertyType,
                    isReadOnly
                );
                if (propertyValue["options"] != null)
                {
                    schemaArrayItem["options"] = propertyValue["options"].DeepClone();
                }
                if (propertyValue["default"] != null)
                {
                    schemaArrayItem["default"] = propertyValue["default"].DeepClone();
                }
                schemaAsArray.Add(schemaArrayItem);

                    if (
                        propertyType == "array"
                        && propertyValue["elementType"] is JObject arrayElementType
                        && arrayElementType["type"]?.ToString().ToLower() == "document"
                    )
                    {
                        documentSchemaAsArray.Add(
                            rtrWflSch_ParseDocumentSchemaItem(propertyName, displayName, isReadOnly)
                        );
                    }
                }
            }

            // Add the approvals array into the launchSchema properties with a single schema object for items
            launchSchema["launchApprovals"] = new JObject
            {
                ["type"] = "array",
                ["title"] = "Workflow Approval Roles",
                ["description"] = "The approval roles for this workflow.",
                ["x-ms-visibility"] = "important",
                ["items"] = new JObject
                {
                    ["type"] = "object",
                    ["title"] = "Approval Role",
                    ["description"] = "An approval role on this workflow.",
                    ["x-ms-visibility"] = "important",
                    ["properties"] = new JObject
                    {
                        ["roleName"] = new JObject
                        {
                            ["type"] = "string",
                            ["title"] = "Property Name",
                            ["description"] = "The system name of the role property.",
                            ["x-ms-visibility"] = "important"
                        },
                        ["assignee"] = new JObject
                        {
                            ["type"] = "string",
                            ["title"] = "Assignee ID",
                            ["description"] = "The ID of the user assigned to this approval role.",
                            ["x-ms-visibility"] = "important",
                            ["x-ms-dynamic-values"] = new JObject
                            {
                                ["operationId"] = "ListUsers",
                                ["value-collection"] = "Resources",
                                ["value-path"] = "id",
                                ["value-title"] = "username"
                            }
                        }
                    },
                    ["required"] = new JArray
                    {
                        "roleName",
                        "assignee"
                    }
                }
            };

            // Compose required array: always include counterpartyName, plus any with required: always
            var launchRequired = new JArray { "counterpartyName" };
            foreach (var reqProp in requiredProperties)
            {
                if (!launchRequired.Contains(reqProp))
                {
                    launchRequired.Add(reqProp);
                }
            }
            body["launchSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = launchSchema,
                ["required"] = launchRequired
            };
            body["formattedSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = formattedSchema
            };
            body["schemaAsArray"] = schemaAsArray;
            body["documentSchemaAsArray"] = documentSchemaAsArray;
        }

        return body;
    }

    /// <summary>
    /// Creates the schema-array item shape used by both workflow-schema and workflow
    /// response helpers when exposing field metadata as arrays.
    /// </summary>
    private JObject CreateWorkflowSchemaArrayItem(
        string systemName,
        string displayName,
        string type,
        bool isReadOnly
    )
    {
        return new JObject
        {
            ["systemName"] = systemName,
            ["displayName"] = displayName,
            ["type"] = type,
            ["readOnly"] = isReadOnly
        };
    }

    /// <summary>
    /// Creates the document-array metadata item shape shared by workflow schema helpers.
    /// </summary>
    private JObject CreateWorkflowDocumentSchemaItem(
        string systemName,
        string displayName,
        bool isReadOnly
    )
    {
        return new JObject
        {
            ["systemName"] = systemName,
            ["displayName"] = displayName,
            ["readOnly"] = isReadOnly
        };
    }

    /// <summary>
    /// Builds the common OpenAPI schema used for workflow address properties.
    /// Both workflow schema discovery and workflow response shaping use this exact structure.
    /// </summary>
    private JObject FormatWorkflowAddressPropertySchema(string displayName)
    {
        return new JObject
        {
            ["type"] = "object",
            ["title"] = displayName,
            ["description"] = $"The {displayName}.",
            ["x-ms-visibility"] = "important",
            ["properties"] = new JObject
            {
                ["lines"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject
                    {
                        ["type"] = "string",
                        ["title"] = $"{displayName} Line",
                        ["x-ms-visibility"] = "important",
                        ["description"] = $"An address line of {displayName}."
                    }
                },
                ["locality"] = new JObject
                {
                    ["type"] = "string",
                    ["title"] = "Locality",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The locality of {displayName}."
                },
                ["region"] = new JObject
                {
                    ["type"] = "string",
                    ["title"] = "Region",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The region of {displayName}."
                },
                ["postcode"] = new JObject
                {
                    ["type"] = "string",
                    ["title"] = "Postcode",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The postcode of {displayName}."
                },
                ["country"] = new JObject
                {
                    ["type"] = "string",
                    ["title"] = "Country",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The country of {displayName}."
                }
            }
        };
    }

    /// <summary>
    /// Builds the common OpenAPI schema used for workflow monetary amount properties.
    /// </summary>
    private JObject FormatWorkflowMonetaryAmountPropertySchema(string displayName)
    {
        return new JObject
        {
            ["type"] = "object",
            ["title"] = displayName,
            ["description"] = $"The {displayName}.",
            ["x-ms-visibility"] = "important",
            ["properties"] = new JObject
            {
                ["amount"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Amount",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The amount of {displayName}."
                },
                ["currency"] = new JObject
                {
                    ["type"] = "string",
                    ["title"] = "Currency",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The currency of {displayName}."
                }
            }
        };
    }

    /// <summary>
    /// Builds the common OpenAPI schema used for workflow duration properties.
    /// </summary>
    private JObject FormatWorkflowDurationPropertySchema(string displayName)
    {
        return new JObject
        {
            ["type"] = "object",
            ["title"] = displayName,
            ["description"] = $"The {displayName}.",
            ["x-ms-visibility"] = "important",
            ["properties"] = new JObject
            {
                ["years"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Years",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The years of {displayName}."
                },
                ["months"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Months",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The months of {displayName}."
                },
                ["weeks"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Weeks",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The weeks of {displayName}."
                },
                ["days"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Days",
                    ["x-ms-visibility"] = "important",
                    ["description"] = $"The days of {displayName}."
                }
            }
        };
    }

    /// <summary>
    /// Builds the common primitive OpenAPI schema used for workflow string/number/date/etc. fields.
    /// </summary>
    private JObject FormatWorkflowBasicPropertySchema(string propertyType, string displayName)
    {
        var formattedProperty = new JObject
        {
            ["title"] = displayName,
            ["description"] = $"The {displayName}.",
            ["x-ms-visibility"] = "important"
        };

        switch (propertyType)
        {
            case "string":
                formattedProperty["type"] = "string";
                break;
            case "number":
            case "integer":
                formattedProperty["type"] = "number";
                break;
            case "boolean":
                formattedProperty["type"] = "boolean";
                break;
            case "date":
                formattedProperty["type"] = "string";
                formattedProperty["format"] = "date-time";
                break;
            default:
                formattedProperty["type"] = "string";
                break;
        }

        return formattedProperty;
    }

    /// <summary>
    /// Shared dispatcher for workflow-style property schemas. The caller supplies the
    /// array formatter because launch-schema, workflow-schema, and workflow-response
    /// document arrays are intentionally not identical.
    /// </summary>
    private JObject FormatWorkflowPropertySchemaCore(
        string propertyType,
        string displayName,
        string propertyName,
        JObject propertyValue,
        Func<string, string, JObject, JObject> arrayFormatter
    )
    {
        switch (propertyType)
        {
            case "array":
                return arrayFormatter(displayName, propertyName, propertyValue);
            case "address":
                return FormatWorkflowAddressPropertySchema(displayName);
            case "monetaryamount":
                return FormatWorkflowMonetaryAmountPropertySchema(displayName);
            case "duration":
                return FormatWorkflowDurationPropertySchema(displayName);
            default:
                return FormatWorkflowBasicPropertySchema(propertyType, displayName);
        }
    }

    /// <summary>
    /// Shared array-property formatter for workflow-style schemas. The caller supplies the
    /// document-array and table formatters because those differ by route family.
    /// </summary>
    private JObject FormatWorkflowArrayPropertySchemaCore(
        string displayName,
        string propertyName,
        JObject propertyValue,
        Func<string, string, JObject> documentArrayFormatter,
        Func<string, string, JObject, JObject> tableFormatter
    )
    {
        var elementType = propertyValue["elementType"] as JObject;
        if (elementType != null)
        {
            string elementTypeString = elementType["type"].ToString().ToLower();
            if (elementTypeString == "document")
            {
                return documentArrayFormatter(displayName, propertyName);
            }
            else if (elementTypeString == "object")
            {
                return tableFormatter(displayName, propertyName, propertyValue);
            }
            else
            {
                return new JObject
                {
                    ["type"] = "array",
                    ["title"] = displayName,
                    ["description"] = $"The {displayName}.",
                    ["x-ms-visibility"] = "important",
                    ["items"] = FormatWorkflowBasicPropertySchema(
                        elementTypeString,
                        $"{displayName} Item"
                    )
                };
            }
        }

        return new JObject();
    }

    /// <summary>
    /// Formats one workflow schema property for the launchSchema output used when
    /// creating workflows through the connector.
    /// </summary>
    private JObject rtrWflSch_FormatLaunchProperty(
        string propertyType,
        string displayName,
        string propertyName,
        JObject propertyValue
    )
    {
        return FormatWorkflowPropertySchemaCore(
            propertyType,
            displayName,
            propertyName,
            propertyValue,
            rtrWflSch_FormatArrayLaunchProperty
        );
    }

    /// <summary>
    /// Formats an array-typed workflow schema property for launchSchema output.
    /// </summary>
    private JObject rtrWflSch_FormatArrayLaunchProperty(
        string displayName,
        string propertyName,
        JObject propertyValue
    )
    {
        return FormatWorkflowArrayPropertySchemaCore(
            displayName,
            propertyName,
            propertyValue,
            rtrWflSch_FormatDocumentArrayLaunchProperty,
            rtrWflSch_FormatTableProperty
        );
    }

    /// <summary>
    /// Creates the launchSchema shape for document upload arrays on workflow schemas.
    /// </summary>
    private JObject rtrWflSch_FormatDocumentArrayLaunchProperty(
        string displayName,
        string propertyName
    )
    {
        return new JObject
        {
            ["type"] = "array",
            ["title"] = displayName,
            ["description"] = $"The {displayName} files.",
            ["x-ms-visibility"] = "important",
            ["items"] = new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray { "fileName", "fileContent" },
                ["properties"] = new JObject
                {
                    ["fileName"] = new JObject
                    {
                        ["type"] = "string",
                        ["title"] = "File Name",
                        ["description"] = "The name of the file.",
                        ["x-ms-visibility"] = "important"
                    },
                    ["fileContent"] = new JObject
                    {
                        ["type"] = "string",
                        ["format"] = "byte",
                        ["title"] = "File Content",
                        ["description"] = "The content of the file (base64 encoded).",
                        ["x-ms-visibility"] = "important"
                    }
                }
            }
        };
    }

    /// <summary>
    /// Creates the workflow schema table-array shape for launchSchema and formattedSchema
    /// outputs.
    /// </summary>
    private JObject rtrWflSch_FormatTableProperty(
        string displayName,
        string propertyName,
        JObject propertyValue
    )
    {
        var elementTypeSchema = propertyValue["elementType"]["schema"] as JObject;
        var itemsObject = new JObject();

        foreach (var column in elementTypeSchema.Properties())
        {
            var columnSchema = column.Value as JObject;
            if (columnSchema != null)
            {
                string columnDisplayName = columnSchema["displayName"].ToString();
                string columnType = columnSchema["type"].ToString().ToLower();

                // Handle special types
                if (columnType == "monetaryamount")
                {
                    itemsObject[column.Name] = rtrWflSch_FormatMonetaryAmountProperty(
                        columnDisplayName,
                        column.Name
                    );
                }
                else if (columnType == "duration")
                {
                    itemsObject[column.Name] = rtrWflSch_FormatDurationProperty(
                        columnDisplayName,
                        column.Name
                    );
                }
                else if (columnType == "address")
                {
                    itemsObject[column.Name] = rtrWflSch_FormatAddressProperty(
                        columnDisplayName,
                        column.Name
                    );
                }
                else
                {
                    JObject formattedColumn = rtrWflSch_FormatBasicProperty(
                        columnType,
                        columnDisplayName,
                        column.Name
                    );
                    itemsObject[column.Name] = formattedColumn;
                }
            }
        }

        return new JObject
        {
            ["type"] = "array",
            ["title"] = displayName,
            ["description"] = $"The {displayName}.",
            ["x-ms-visibility"] = "important",
            ["items"] = new JObject { ["type"] = "object", ["properties"] = itemsObject }
        };
    }

    /// <summary>
    /// Formats a workflow schema address property using the shared workflow address shape.
    /// </summary>
    private JObject rtrWflSch_FormatAddressProperty(string displayName, string propertyName)
    {
        return FormatWorkflowAddressPropertySchema(displayName);
    }

    /// <summary>
    /// Formats a workflow schema monetary amount property using the shared schema shape.
    /// </summary>
    private JObject rtrWflSch_FormatMonetaryAmountProperty(string displayName, string propertyName)
    {
        return FormatWorkflowMonetaryAmountPropertySchema(displayName);
    }

    /// <summary>
    /// Formats a workflow schema duration property using the shared expanded duration shape.
    /// </summary>
    private JObject rtrWflSch_FormatDurationProperty(string displayName, string propertyName)
    {
        return FormatWorkflowDurationPropertySchema(displayName);
    }

    /// <summary>
    /// Formats a primitive workflow schema property using the shared basic property rules.
    /// </summary>
    private JObject rtrWflSch_FormatBasicProperty(
        string propertyType,
        string displayName,
        string propertyName
    )
    {
        return FormatWorkflowBasicPropertySchema(propertyType, displayName);
    }

    /// <summary>
    /// Creates one schemaAsArray item for a workflow schema property.
    /// </summary>
    private JObject rtrWflSch_ParseSchemaArrayItem(
        string systemName,
        string displayName,
        string type,
        bool isReadOnly
    )
    {
        return CreateWorkflowSchemaArrayItem(systemName, displayName, type, isReadOnly);
    }

    /// <summary>
    /// Creates one documentSchemaAsArray item for a workflow document field.
    /// </summary>
    private JObject rtrWflSch_ParseDocumentSchemaItem(
        string systemName,
        string displayName,
        bool isReadOnly
    )
    {
        return CreateWorkflowDocumentSchemaItem(systemName, displayName, isReadOnly);
    }

    /// <summary>
    /// Formats one workflow schema property for the formattedSchema output.
    /// </summary>
    private JObject rtrWflSch_FormatProperty(
        string propertyType,
        string displayName,
        string propertyName,
        JObject propertyValue
    )
    {
        return FormatWorkflowPropertySchemaCore(
            propertyType,
            displayName,
            propertyName,
            propertyValue,
            rtrWflSch_FormatArrayProperty
        );
    }

    /// <summary>
    /// Formats an array-typed workflow schema property for the formattedSchema output.
    /// </summary>
    private JObject rtrWflSch_FormatArrayProperty(
        string displayName,
        string propertyName,
        JObject propertyValue
    )
    {
        return FormatWorkflowArrayPropertySchemaCore(
            displayName,
            propertyName,
            propertyValue,
            rtrWflSch_FormatDocumentArrayProperty,
            rtrWflSch_FormatTableProperty
        );
    }

    /// <summary>
    /// Creates the formattedSchema shape for document arrays returned by workflow schema
    /// discovery.
    /// </summary>
    private JObject rtrWflSch_FormatDocumentArrayProperty(string displayName, string propertyName)
    {
        return new JObject
        {
            ["type"] = "array",
            ["title"] = displayName,
            ["description"] = $"The {displayName} files.",
            ["x-ms-visibility"] = "important",
            ["items"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["filename"] = new JObject
                    {
                        ["type"] = "string",
                        ["title"] = "File Name",
                        ["x-ms-visibility"] = "important",
                        ["description"] = "The name of the file."
                    },
                    ["download"] = new JObject
                    {
                        ["type"] = "string",
                        ["title"] = "Download Link",
                        ["x-ms-visibility"] = "important",
                        ["description"] = "The download link of the file."
                    },
                    ["key"] = new JObject
                    {
                        ["type"] = "string",
                        ["title"] = "Key",
                        ["x-ms-visibility"] = "important",
                        ["description"] = "The key of the file."
                    }
                }
            }
        };
    }

    // ################################################################################
    // List All Workflow ##############################################################
    // ################################################################################

    /// <summary>
    /// Adds the connector's label field to each workflow in the list response.
    /// </summary>
    private JObject lstAllWfl_TransformListAllWorkflowsResponse(JObject body)
    {
        if (body.ContainsKey("list") && body["list"] is JArray list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is JObject workflowObject)
                {
                    string ironcladId = workflowObject["ironcladId"]?.ToString() ?? "";
                    string title = workflowObject["title"]?.ToString() ?? "";
                    workflowObject["label"] = $"{ironcladId}: {title}";
                }
            }
        }

        return body;
    }

    // ################################################################################
    // Retrieve Workflow ##############################################################
    // ################################################################################
    /// <summary>
    /// Expands a retrieved workflow into the connector's formatted schema, attribute, and
    /// document helper outputs.
    /// </summary>
    private JObject rtrWfl_TransformRetrieveWorkflow(JObject body)
    {
        var schema = body["schema"] as JObject;
        if (schema != null)
        {
            var formattedSchema = new JObject();
            var schemaAsArray = new JArray();
            var documentsAsArray = new JArray();

            foreach (var property in schema.Properties())
            {
                rtrWfl_ProcessSchemaProperty(property, formattedSchema, schemaAsArray);
            }

            body["formattedSchema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = formattedSchema
            };
            body["schemaAsArray"] = schemaAsArray;

            // Transform documents from the existing workflow documents
            if (body["attributes"] is JObject attributes)
            {
                // Handle draft documents
                if (attributes["draft"] is JArray draftDocs && draftDocs.Any())
                {
                    var draftDocObject = new JObject
                    {
                        ["systemName"] = "draft",
                        ["displayName"] = "Draft Document",
                        ["readOnly"] = true,
                        ["versions"] = draftDocs
                    };
                    documentsAsArray.Add(draftDocObject);
                }

                // Handle signed documents
                if (attributes["signed"] is JObject signedDoc)
                {
                    var signedDocObject = new JObject
                    {
                        ["systemName"] = "signed",
                        ["displayName"] = "Signed Document",
                        ["readOnly"] = true,
                        ["versions"] = new JArray(signedDoc)
                    };
                    documentsAsArray.Add(signedDocObject);
                }

                // Handle sentSignaturePacket documents
                if (attributes["sentSignaturePacket"] is JArray signaturePacketDocs && signaturePacketDocs.Any())
                {
                    var signaturePacketObject = new JObject
                    {
                        ["systemName"] = "signaturePacket",
                        ["displayName"] = "Signature Packet",
                        ["readOnly"] = true,
                        ["versions"] = signaturePacketDocs
                    };
                    documentsAsArray.Add(signaturePacketObject);
                }
            }

            body["documentsAsArray"] = documentsAsArray;
            body["formattedAttributes"] = rtrWfl_FormatWorkflowAttributes(
                body["attributes"] as JObject,
                formattedSchema
            );
        }

        // Always ensure counterpartyName is present at the root.
        if (body["attributes"] is JObject workflowAttributes && workflowAttributes.ContainsKey("counterpartyName"))
        {
            body["counterpartyName"] = workflowAttributes["counterpartyName"];
        }
        else
        {
            body["counterpartyName"] = null;
        }

        return body;
    }

    /// <summary>
    /// Adds one workflow schema property to the formatted schema and schemaAsArray
    /// outputs for RetrieveWorkflow.
    /// </summary>
    private void rtrWfl_ProcessSchemaProperty(
        JProperty property,
        JObject formattedSchema,
        JArray schemaAsArray
    )
    {
        var propertySchema = property.Value as JObject;
        if (propertySchema != null)
        {
            string displayName = propertySchema["displayName"]?.ToString() ?? property.Name;
            string propertyType = propertySchema["type"]?.ToString().ToLower();
            bool isReadOnly = propertySchema["readOnly"]?.ToObject<bool>() ?? false;

            if (isReadOnly)
            {
                displayName += " (read only)";
            }

            JObject formattedProperty = rtrWfl_FormatPropertyByType(
                propertyType,
                displayName,
                property.Name,
                propertySchema
            );
            formattedProperty["readOnly"] = isReadOnly;
            formattedSchema[property.Name] = formattedProperty;

            schemaAsArray.Add(
                new JObject
                {
                    ["systemName"] = property.Name,
                    ["displayName"] = displayName,
                    ["type"] = propertyType,
                    ["readOnly"] = isReadOnly
                }
            );
        }
    }

    /// <summary>
    /// Dispatches RetrieveWorkflow property formatting based on the Ironclad field type.
    /// </summary>
    private JObject rtrWfl_FormatPropertyByType(
        string propertyType,
        string displayName,
        string propertyName,
        JObject propertySchema
    )
    {
        return FormatWorkflowPropertySchemaCore(
            propertyType,
            displayName,
            propertyName,
            propertySchema,
            rtrWfl_FormatArrayProperty
        );
    }

    /// <summary>
    /// Formats an array-typed RetrieveWorkflow schema property.
    /// </summary>
    private JObject rtrWfl_FormatArrayProperty(
        string displayName,
        string propertyName,
        JObject propertySchema
    )
    {
        return FormatWorkflowArrayPropertySchemaCore(
            displayName,
            propertyName,
            propertySchema,
            rtrWfl_FormatDocumentArrayProperty,
            rtrWfl_FormatTableProperty
        );
    }

    /// <summary>
    /// Creates the RetrieveWorkflow schema shape for document arrays and document version
    /// metadata.
    /// </summary>
    private JObject rtrWfl_FormatDocumentArrayProperty(string displayName, string propertyName)
    {
        return new JObject
        {
            ["type"] = "array",
            ["title"] = displayName,
            ["description"] = $"The {displayName} files.",
            ["x-ms-visibility"] = "important",
            ["items"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["filename"] = new JObject
                    {
                        ["type"] = "string",
                        ["title"] = "File Name",
                        ["x-ms-visibility"] = "important",
                        ["description"] = "The name of the file."
                    },
                    ["version"] = new JObject
                    {
                        ["type"] = "string",
                        ["title"] = "Version",
                        ["x-ms-visibility"] = "important",
                        ["description"] = "The version of the file."
                    },
                    ["versionNumber"] = new JObject
                    {
                        ["type"] = "number",
                        ["title"] = "Version Number",
                        ["x-ms-visibility"] = "important",
                        ["description"] = "The version number of the file."
                    },
                    ["download"] = new JObject
                    {
                        ["type"] = "string",
                        ["title"] = "Download Link",
                        ["x-ms-visibility"] = "important",
                        ["description"] = "The download link of the file."
                    },
                    ["key"] = new JObject
                    {
                        ["type"] = "string",
                        ["title"] = "Key",
                        ["x-ms-visibility"] = "important",
                        ["description"] = "The key of the file."
                    },
                    ["lastModified"] = new JObject
                    {
                        ["type"] = "object",
                        ["title"] = "Last Modified",
                        ["x-ms-visibility"] = "important",
                        ["description"] = "Information on when the file was last modified.",
                        ["properties"] = new JObject
                        {
                            ["timestamp"] = new JObject
                            {
                                ["type"] = "string",
                                ["format"] = "date-time",
                                ["title"] = "Timestamp",
                                ["x-ms-visibility"] = "important",
                                ["description"] = "The date when the file was last modified."
                            },
                            ["author"] = new JObject
                            {
                                ["type"] = "object",
                                ["title"] = "Author",
                                ["x-ms-visibility"] = "important",
                                ["description"] = "The author of the last modification.",
                                ["properties"] = new JObject
                                {
                                    ["displayName"] = new JObject
                                    {
                                        ["type"] = "string",
                                        ["title"] = "Display Name",
                                        ["x-ms-visibility"] = "important",
                                        ["description"] = "The display name of the author."
                                    },
                                    ["email"] = new JObject
                                    {
                                        ["type"] = "string",
                                        ["title"] = "Email",
                                        ["x-ms-visibility"] = "important",
                                        ["description"] = "The email of the author."
                                    },
                                    ["userId"] =
                                        new JObject
                                        {
                                            ["type"] = "string",
                                            ["title"] = "User ID",
                                            ["x-ms-visibility"] = "important",
                                            ["description"] = "The user ID of the author."
                                        }["type"] =
                                        new JObject
                                        {
                                            ["type"] = "string",
                                            ["title"] = "Type",
                                            ["x-ms-visibility"] = "important",
                                            ["description"] = "The type of the file."
                                        }["companyName"] =
                                            new JObject
                                            {
                                                ["type"] = "string",
                                                ["title"] = "Company Name",
                                                ["x-ms-visibility"] = "important",
                                                ["description"] = "The company name of the author."
                                            }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Creates the RetrieveWorkflow schema shape for table-style array properties.
    /// </summary>
    private JObject rtrWfl_FormatTableProperty(
        string displayName,
        string propertyName,
        JObject propertySchema
    )
    {
        var elementTypeSchema = propertySchema["elementType"]["schema"] as JObject;
        var itemsObject = new JObject();

        foreach (var column in elementTypeSchema.Properties())
        {
            var columnSchema = column.Value as JObject;
            if (columnSchema != null)
            {
                string columnDisplayName = columnSchema["displayName"].ToString();
                string columnType = columnSchema["type"].ToString().ToLower();

                JObject formattedColumn = rtrWfl_FormatBasicProperty(
                    columnType,
                    columnDisplayName,
                    column.Name
                );
                itemsObject[column.Name] = formattedColumn;
            }
        }

        return new JObject
        {
            ["type"] = "array",
            ["title"] = displayName,
            ["description"] = $"The {displayName}.",
            ["x-ms-visibility"] = "important",
            ["items"] = new JObject { ["type"] = "object", ["properties"] = itemsObject }
        };
    }

    /// <summary>
    /// Formats a RetrieveWorkflow address property using the shared workflow address schema.
    /// </summary>
    private JObject rtrWfl_FormatAddressProperty(string displayName, string propertyName)
    {
        return FormatWorkflowAddressPropertySchema(displayName);
    }

    /// <summary>
    /// Formats a RetrieveWorkflow monetary amount property using the shared schema shape.
    /// </summary>
    private JObject rtrWfl_FormatMonetaryAmountProperty(string displayName, string propertyName)
    {
        return FormatWorkflowMonetaryAmountPropertySchema(displayName);
    }

    /// <summary>
    /// Formats a RetrieveWorkflow duration property using the shared expanded duration schema.
    /// </summary>
    private JObject rtrWfl_FormatDurationProperty(string displayName, string propertyName)
    {
        return FormatWorkflowDurationPropertySchema(displayName);
    }

    /// <summary>
    /// Formats a primitive RetrieveWorkflow property using the shared workflow schema rules.
    /// </summary>
    private JObject rtrWfl_FormatBasicProperty(
        string propertyType,
        string displayName,
        string propertyName
    )
    {
        return FormatWorkflowBasicPropertySchema(propertyType, displayName);
    }

    /// <summary>
    /// Creates one schemaAsArray item for the RetrieveWorkflow response.
    /// </summary>
    private JObject rtrWfl_CreateSchemaArrayItem(
        string systemName,
        string displayName,
        string type,
        bool isReadOnly
    )
    {
        return CreateWorkflowSchemaArrayItem(systemName, displayName, type, isReadOnly);
    }

    /// <summary>
    /// Creates one document metadata array item for the RetrieveWorkflow response.
    /// </summary>
    private JObject rtrWfl_CreateDocumentSchemaItem(
        string systemName,
        string displayName,
        bool isReadOnly
    )
    {
        return CreateWorkflowDocumentSchemaItem(systemName, displayName, isReadOnly);
    }

    /// <summary>
    /// Formats workflow attribute values using the already-built formatted schema so the
    /// response values match the connector's helper outputs.
    /// </summary>
    private JObject rtrWfl_FormatWorkflowAttributes(JObject attributes, JObject formattedSchema)
    {
        if (attributes == null || formattedSchema == null)
        {
            return new JObject();
        }

        var formattedAttributes = new JObject();

        foreach (var property in formattedSchema.Properties())
        {
            var propertySchema = property.Value as JObject;
            if (propertySchema != null && attributes.ContainsKey(property.Name))
            {
                var attributeValue = attributes[property.Name];
                formattedAttributes[property.Name] = rtrWfl_FormatAttributeValue(
                    attributeValue,
                    propertySchema
                );
            }
        }

        return formattedAttributes;
    }

    /// <summary>
    /// Formats a workflow attribute value based on the corresponding formatted schema node.
    /// </summary>
    private JToken rtrWfl_FormatAttributeValue(JToken value, JObject schema)
    {
        string propertyType = schema["type"]?.ToString().ToLower();

        switch (propertyType)
        {
            case "array":
                return rtrWfl_FormatArrayAttributeValue(value, schema);
            case "object":
                return rtrWfl_FormatObjectAttributeValue(value, schema);
            default:
                return value;
        }
    }

    /// <summary>
    /// Formats an array-valued workflow attribute, including nested object items when
    /// the schema declares them.
    /// </summary>
    private JArray rtrWfl_FormatArrayAttributeValue(JToken value, JObject schema)
    {
        var formattedArray = new JArray();
        var items = value as JArray;

        if (items != null)
        {
            var elementType = schema["items"] as JObject;
            string itemType = elementType?["type"]?.ToString().ToLower();

            foreach (var item in items)
            {
                if (itemType == "object")
                {
                    formattedArray.Add(rtrWfl_FormatObjectAttributeValue(item, elementType));
                }
                else
                {
                    formattedArray.Add(item);
                }
            }
        }

        return formattedArray;
    }

    /// <summary>
    /// Formats an object-valued workflow attribute using the formatted child property
    /// definitions from the schema.
    /// </summary>
    private JObject rtrWfl_FormatObjectAttributeValue(JToken value, JObject schema)
    {
        var formattedObject = new JObject();
        var objectValue = value as JObject;

        if (objectValue != null && schema["properties"] is JObject propertiesSchema)
        {
            foreach (var property in propertiesSchema.Properties())
            {
                if (objectValue.ContainsKey(property.Name))
                {
                    formattedObject[property.Name] = rtrWfl_FormatAttributeValue(
                        objectValue[property.Name],
                        property.Value as JObject
                    );
                }
            }
        }

        return formattedObject;
    }

    // ################################################################################
    // Retrieve Record Schema #########################################################
    // ################################################################################

    /// <summary>
    /// Repoints the formatted-record-schema route to the raw record metadata endpoint.
    /// The response is reformatted afterward so callers still receive the existing schema shape.
    /// </summary>
    private Task rtrRcdFmtSch_TransformRequest()
    {
        var uri = this.Context.Request.RequestUri;
        var metadataUri = new Uri(
            uri.GetLeftPart(UriPartial.Authority) + "/public/api/v1/records/metadata" + uri.Query
        );
        this.Context.Request.RequestUri = metadataUri;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the formatted record schema response from raw record metadata while preserving
    /// the connector's current record-properties query behavior.
    /// </summary>
    private async Task rtrRcdFmtSch_TransformResponse(HttpResponseMessage response)
    {
        var propertiesQuery = GetRequestQueryValue(RecordPropertiesQueryParameter);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var metadata = JObject.Parse(content);
        var transformedData = rtrRcdSch_TransformRetrieveRecordSchemas(
            metadata,
            propertiesQuery,
            true
        );
        var formattedSchema =
            transformedData["formattedSchema"] as JObject ?? rtrRcdSch_CreateEmptyFormattedSchema();

        response.Content = CreateJsonContent(formattedSchema.ToString());
    }

    /// <summary>
    /// Handles RetrieveRecordSchemas by validating requested property names and returning
    /// the connector's enriched schema representation.
    /// </summary>
    private async Task<HttpResponseMessage> rtrRcdSch_HandleRequest()
    {
        try
        {
            var propertiesQuery = GetRequestQueryValue(RecordPropertiesQueryParameter);

            var metadata = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);

            // If we have a query, validate all properties exist
            if (!string.IsNullOrWhiteSpace(propertiesQuery))
            {
                var requestedItems = propertiesQuery.Split(',').Select(p => p.Trim()).ToList();
                var properties = metadata["properties"] as JObject;
                var attachments = metadata["attachments"] as JObject;

                foreach (var item in requestedItems)
                {
                    bool exists = false;

                    // Check in properties (including clauses)
                    if (properties != null && properties.ContainsKey(item))
                    {
                        var propObj = properties[item] as JObject;
                        if (propObj != null)
                        {
                            exists =
                                propObj["type"] != null
                                && (
                                    propObj["resolvesTo"] == null
                                    || propObj["resolvesTo"].Type == JTokenType.Null
                                );
                        }
                    }
                    // Check in attachments
                    else if (attachments != null && attachments.ContainsKey(item))
                    {
                        var attachmentObj = attachments[item] as JObject;
                        exists = attachmentObj != null;
                    }

                    if (!exists)
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = CreateJsonContent(
                                new JObject
                                {
                                    ["error"] = new JObject
                                    {
                                        ["message"] = $"Property '{item}' not found"
                                    }
                                }.ToString()
                            )
                        };
                    }
                }
            }

            // Transform the metadata
            var transformedData = rtrRcdSch_TransformRetrieveRecordSchemas(
                metadata,
                propertiesQuery
            );

            // Create response
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent(transformedData.ToString())
            };
        }
        catch (Exception ex)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent(
                    new JObject { ["error"] = new JObject { ["message"] = ex.Message } }.ToString()
                )
            };
        }
    }
    /// <summary>
    /// Returns the empty formattedSchema object used when record schema filters are not
    /// provided or no formatted record fields are emitted.
    /// </summary>
    private JObject rtrRcdSch_CreateEmptyFormattedSchema()
    {
        return new JObject
        {
            ["type"] = "object",
            ["description"] =
                "The record schema formatted for compatibility with the OpenAPI standard.",
            ["x-ms-visibility"] = "important",
            ["properties"] = new JObject()
        };
    }

    /// <summary>
    /// Rewrites the record metadata response into the connector's formatted property,
    /// clause, attachment, and optional formattedSchema outputs.
    /// </summary>
    private JObject rtrRcdSch_TransformRetrieveRecordSchemas(
        JObject body,
        string propertiesQuery,
        bool includeFormattedSchema = false
    )
    {
        var properties = body["properties"] as JObject;
        if (properties != null)
        {
            var formattedProperties = new JArray();
            var formattedClauses = new JArray();

            // Get requested items if filtering is needed
            var requestedItems = !string.IsNullOrWhiteSpace(propertiesQuery)
                ? propertiesQuery.Split(',').Select(p => p.Trim()).ToList()
                : new List<string>();

            foreach (var prop in properties.Properties())
            {
                var propObj = prop.Value as JObject;
                if (
                    propObj != null
                    && (
                        propObj["resolvesTo"] == null
                        || propObj["resolvesTo"].Type == JTokenType.Null
                    )
                )
                {
                    bool isVisible = propObj["visible"]?.ToObject<bool>() ?? true;

                    if (isVisible)
                    {
                        string originalType = propObj["type"]?.ToString().ToLower() ?? "unknown";
                        string effectiveType = originalType == "address" ? "string" : originalType;
                        string displayName = propObj["displayName"]?.ToString() ?? prop.Name;

                        var newObj = new JObject
                        {
                            ["systemName"] = prop.Name,
                            ["type"] = effectiveType,
                            ["displayName"] = displayName,
                            ["description"] = $"The {displayName}.",
                            ["ironcladType"] = propObj["type"]
                        };

                        string formattedType = effectiveType.Replace("_", " ");
                        newObj["label"] = $"{displayName} ({formattedType})";
                        newObj["typedPropertyName"] = $"{prop.Name}?{effectiveType}";

                        foreach (var subProp in propObj.Properties())
                        {
                            if (!newObj.ContainsKey(subProp.Name))
                            {
                                newObj[subProp.Name] = subProp.Value;
                            }
                        }

                        // Only add if no filter or if property is in filter
                        if (!requestedItems.Any() || requestedItems.Contains(prop.Name))
                        {
                            if (effectiveType == "clause")
                            {
                                formattedClauses.Add(newObj);
                            }
                            else if (effectiveType != "document")
                            {
                                formattedProperties.Add(newObj);
                            }
                        }
                    }
                }
            }

            // Handle record types
            var recordTypes = body["recordTypes"] as JObject;
            if (recordTypes != null)
            {
                var formattedRecordTypes = new JArray();
                foreach (var rt in recordTypes.Properties())
                {
                    var rtObj = rt.Value as JObject;
                    if (rtObj != null)
                    {
                        var displayName = rtObj["displayName"]?.ToString() ?? rt.Name;
                        var newObj = new JObject(rtObj)
                        {
                            ["systemName"] = rt.Name,
                            ["displayName"] = displayName,
                            ["description"] = $"The {displayName} record type."
                        };
                        formattedRecordTypes.Add(newObj);
                    }
                }
                body["formattedRecordTypes"] = formattedRecordTypes;
            }

            // Handle attachments
            var attachments = body["attachments"] as JObject;
            var formattedAttachments = new JArray();
            if (attachments != null)
            {
                foreach (var attachment in attachments.Properties())
                {
                    var attachmentObj = attachment.Value as JObject;
                    if (attachmentObj != null)
                    {
                        var displayName =
                            attachmentObj["displayName"]?.ToString() ?? attachment.Name;
                        var newObj = new JObject
                        {
                            ["systemName"] = attachment.Name,
                            ["displayName"] = displayName,
                            ["description"] = $"The {displayName} attachment."
                        };
                        if (!requestedItems.Any() || requestedItems.Contains(attachment.Name))
                        {
                            formattedAttachments.Add(newObj);
                        }
                    }
                }
            }

            body["formattedProperties"] = formattedProperties;
            body["formattedClauses"] = formattedClauses;
            body["formattedAttachments"] = formattedAttachments;
            body.Remove("properties");
            body.Remove("recordTypes");
            body.Remove("attachments");
            body.Remove("requestedProperties");

            if (includeFormattedSchema)
            {
                if (!string.IsNullOrWhiteSpace(propertiesQuery))
                {
                    var propertiesSchema = new JObject();

                    foreach (var prop in formattedProperties)
                    {
                        var propertyObj = prop as JObject;
                        var propertyName = propertyObj["systemName"].ToString();
                        var propertyType = propertyObj["type"].ToString().ToLower();
                        var displayName = propertyObj["displayName"].ToString();
                        var description =
                            propertyObj["description"]?.ToString() ?? $"The {displayName}.";

                        JObject schemaProperty = FormatRecordPropertySchemaCore(
                            propertyType,
                            displayName,
                            description,
                            (recordDisplayName, recordDescription) =>
                                new JObject
                                {
                                    ["type"] = "object",
                                    ["title"] = recordDisplayName,
                                    ["description"] = recordDescription,
                                    ["x-ms-visibility"] = "important",
                                    ["properties"] = new JObject
                                    {
                                        ["amount"] = new JObject
                                        {
                                            ["type"] = "number",
                                            ["title"] = "Amount",
                                            ["description"] =
                                                $"The monetary amount value for {recordDisplayName}."
                                        },
                                        ["currency"] = new JObject
                                        {
                                            ["type"] = "string",
                                            ["title"] = "Currency",
                                            ["description"] =
                                                $"The currency code for {recordDisplayName}."
                                        }
                                    }
                                },
                            (recordDisplayName, recordDescription) =>
                                new JObject
                                {
                                    ["type"] = "object",
                                    ["title"] = recordDisplayName,
                                    ["description"] = recordDescription,
                                    ["x-ms-visibility"] = "important",
                                    ["properties"] = new JObject
                                    {
                                        ["isoDuration"] = new JObject
                                        {
                                            ["type"] = "string",
                                            ["title"] = "ISO Duration",
                                            ["description"] =
                                                $"The ISO 8601 duration representation for {recordDisplayName}."
                                        },
                                        ["years"] = new JObject
                                        {
                                            ["type"] = "number",
                                            ["title"] = "Years",
                                            ["description"] =
                                                $"The number of years in {recordDisplayName}."
                                        },
                                        ["months"] = new JObject
                                        {
                                            ["type"] = "number",
                                            ["title"] = "Months",
                                            ["description"] =
                                                $"The number of months in {recordDisplayName}."
                                        },
                                        ["weeks"] = new JObject
                                        {
                                            ["type"] = "number",
                                            ["title"] = "Weeks",
                                            ["description"] =
                                                $"The number of weeks in {recordDisplayName}."
                                        },
                                        ["days"] = new JObject
                                        {
                                            ["type"] = "number",
                                            ["title"] = "Days",
                                            ["description"] =
                                                $"The number of days in {recordDisplayName}."
                                        }
                                    }
                                }
                        );

                        propertiesSchema[propertyName] = schemaProperty;
                    }

                    body["formattedSchema"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] =
                            "The record schema formatted for compatibility with the OpenAPI standard.",
                        ["x-ms-visibility"] = "important",
                        ["properties"] = new JObject
                        {
                            ["recordProperties"] = new JObject
                            {
                                ["type"] = "object",
                                ["title"] = "Properties",
                                ["description"] = "The properties of the record.",
                                ["x-ms-visibility"] = "important",
                                ["properties"] = propertiesSchema
                            },
                            ["recordClauses"] = rtrRcdSch_FormatRecordClausesSchema(
                                formattedClauses
                            ),
                            ["recordAttachments"] = rtrRcdSch_CreateAttachmentSchema(
                                formattedAttachments
                            )
                        }
                    };
                }
                else
                {
                    body["formattedSchema"] = rtrRcdSch_CreateEmptyFormattedSchema();
                }
            }
        }

        return body;
    }

    /// <summary>
    /// Builds the formattedSchema recordClauses object from the formatted clause metadata
    /// returned by RetrieveRecordSchemas.
    /// </summary>
    private JObject rtrRcdSch_FormatRecordClausesSchema(JArray clauses)
    {
        var clausesSchema = new JObject();
        foreach (var clause in clauses)
        {
            var clauseObj = clause as JObject;
            var clauseName = clauseObj["systemName"].ToString();
            var displayName = clauseObj["displayName"].ToString();

            clausesSchema[clauseName] = new JObject
            {
                ["type"] = "object",
                ["title"] = displayName,
                ["description"] = $"The {displayName} clause.",
                ["x-ms-visibility"] = "important",
                ["properties"] = CreateClauseSchemaProperties(
                    $"The display name of the {displayName} clause.",
                    $"The description of the {displayName} clause.",
                    $"The text content of the {displayName} clause.",
                    $"The source of the {displayName} clause.",
                    $"The type of the {displayName} clause.",
                    null,
                    null,
                    $"The language position type of the {displayName} clause."
                )
            };
        }

        return new JObject
        {
            ["type"] = "object",
            ["title"] = "Clauses",
            ["description"] = "The clauses of the record.",
            ["x-ms-visibility"] = "important",
            ["properties"] = clausesSchema
        };
    }

    /// <summary>
    /// Builds the formattedSchema recordAttachments object from the formatted attachment
    /// metadata returned by RetrieveRecordSchemas.
    /// </summary>
    private JObject rtrRcdSch_CreateAttachmentSchema(JArray attachments)
    {
        var attachmentsSchema = new JObject();
        foreach (var attachment in attachments)
        {
            var attachmentObj = attachment as JObject;
            var attachmentName = attachmentObj["systemName"].ToString();
            var displayName = attachmentObj["displayName"].ToString();

            attachmentsSchema[attachmentName] = CreateRecordAttachmentSchemaObject(
                displayName,
                $"The {displayName} attachment.",
                false,
                $"The unique key identifier for the {displayName} attachment."
            );
        }

        return new JObject
        {
            ["type"] = "object",
            ["title"] = "Attachments",
            ["description"] = "The attachments associated with the record.",
            ["x-ms-visibility"] = "important",
            ["properties"] = attachmentsSchema
        };
    }

    // ################################################################################
    // Retrieve All Records ###########################################################
    // ################################################################################

    /// <summary>
    /// Handles ListAllRecords metadata-driven shaping, including validation of the current
    /// record-properties query parameter before the connector builds its formatted response.
    /// </summary>
    private async Task<HttpResponseMessage> lstAllRcd_HandleRequest()
    {
        try
        {
            var propertiesQuery = GetRequestQueryValue(RecordPropertiesQueryParameter);

            var metadata = await ReadRequestBodyAsObjectAsync().ConfigureAwait(false);

            // If we have a query, validate all properties exist
            if (!string.IsNullOrWhiteSpace(propertiesQuery))
            {
                var requestedItems = propertiesQuery.Split(',').Select(p => p.Trim()).ToList();
                var properties = metadata["properties"] as JObject;
                var attachments = metadata["attachments"] as JObject;

                foreach (var item in requestedItems)
                {
                    bool exists = false;

                    // Check in properties (including clauses)
                    if (properties != null && properties.ContainsKey(item))
                    {
                        var propObj = properties[item] as JObject;
                        if (propObj != null)
                        {
                            exists =
                                propObj["type"] != null
                                && (
                                    propObj["resolvesTo"] == null
                                    || propObj["resolvesTo"].Type == JTokenType.Null
                                );
                        }
                    }
                    // Check in attachments
                    else if (attachments != null && attachments.ContainsKey(item))
                    {
                        var attachmentObj = attachments[item] as JObject;
                        exists = attachmentObj != null;
                    }

                    if (!exists)
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = CreateJsonContent(
                                new JObject
                                {
                                    ["error"] = new JObject
                                    {
                                        ["message"] = $"Property '{item}' not found"
                                    }
                                }.ToString()
                            )
                        };
                    }
                }
            }

            var responseBody = metadata;
            var transformedBody = lstAllRcd_TransformListAllRecordsResponse(
                responseBody,
                propertiesQuery
            );

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CreateJsonContent(transformedBody.ToString())
            };
        }
        catch (Exception ex)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = CreateJsonContent(
                    new JObject { ["error"] = new JObject { ["message"] = ex.Message } }.ToString()
                )
            };
        }
    }
    /// <summary>
    /// Adds connector-specific labels, formatted attachments, and optional formatted
    /// property outputs to each record in the list response.
    /// </summary>
    private JObject lstAllRcd_TransformListAllRecordsResponse(JObject body, string propertiesQuery)
    {
        // Add the properties query to the response
        body["requestedProperties"] = propertiesQuery;

        var requestedProperties = !string.IsNullOrWhiteSpace(propertiesQuery)
            ? propertiesQuery.Split(',').Select(p => p.Trim()).ToList()
            : new List<string>();

        if (body.ContainsKey("list") && body["list"] is JArray list)
        {
            foreach (var record in list.Children<JObject>())
            {
                // Do not transform the properties property - keep it as is
                var originalProperties = record["properties"] as JObject;

                // Handle counterpartyName at root level if it exists
                if (
                    originalProperties != null
                    && originalProperties.ContainsKey("counterpartyName")
                    && originalProperties["counterpartyName"] is JObject counterpartyProp
                    && counterpartyProp["value"] != null
                )
                {
                    record["counterpartyName"] = counterpartyProp["value"];
                }

                // Add label
                string ironcladId = record["ironcladId"]?.ToString() ?? "";
                string name = record["name"]?.ToString() ?? "";
                record["label"] = $"{ironcladId}: {name}";

                // Format attachments array
                var recordAttachmentsObj = record["attachments"] as JObject;
                if (recordAttachmentsObj != null)
                {
                    var attachmentsArray = new JArray();
                    foreach (var attachment in recordAttachmentsObj)
                    {
                        attachmentsArray.Add(
                            new JObject
                            {
                                ["displayName"] = attachment.Value["displayName"] ?? attachment.Key,
                                ["name"] = attachment.Key,
                                ["key"] = attachment.Key // Add the key property
                            }
                        );
                    }
                    record["formattedAttachments"] = attachmentsArray;
                }

                // Handle formatted properties if filtering is requested
                if (requestedProperties.Any())
                {
                    var formattedProperties = new JObject();

                    // Initialize all containers
                    var recordProperties = new JObject();
                    var recordClauses = new JObject();
                    var recordAttachments = new JObject();

                    foreach (var propertyName in requestedProperties)
                    {
                        if (
                            originalProperties != null
                            && originalProperties.ContainsKey(propertyName)
                        )
                        {
                            var property = originalProperties[propertyName] as JObject;
                            if (property != null)
                            {
                                var propertyType = property["type"]?.ToString().ToLower();

                                if (propertyType == "clause")
                                {
                                    // Handle clause properties
                                    recordClauses[propertyName] = lstAllRcd_FormatClauseProperty(
                                        propertyName,
                                        property
                                    );
                                }
                                else
                                {
                                    // Handle regular properties
                                    recordProperties[propertyName] = lstAllRcd_FormatPropertyValue(
                                        property
                                    );
                                }
                            }
                        }

                        // Handle attachments separately
                        if (
                            recordAttachmentsObj != null
                            && recordAttachmentsObj.ContainsKey(propertyName)
                        )
                        {
                            var attachment = recordAttachmentsObj[propertyName] as JObject;
                            recordAttachments[propertyName] = new JObject
                            {
                                ["filename"] = attachment["filename"],
                                ["contentType"] = attachment["contentType"],
                                ["href"] = attachment["href"],
                                ["displayName"] = attachment["displayName"] ?? propertyName,
                                ["key"] = propertyName // Add the key property
                            };
                        }
                    }

                    // Always include all three properties in formattedProperties
                    formattedProperties["recordProperties"] = recordProperties;
                    formattedProperties["recordClauses"] = recordClauses;
                    formattedProperties["recordAttachments"] = recordAttachments;

                    record["formattedProperties"] = formattedProperties;
                }
            }
        }

        return body;
    }

    /// <summary>
    /// Formats one listed record property value, expanding duration objects while leaving
    /// other values in their current connector shape.
    /// </summary>
    private JToken lstAllRcd_FormatPropertyValue(JObject property)
    {
        if (property == null || !property.ContainsKey("type") || !property.ContainsKey("value"))
            return null;

        string propertyType = property["type"].ToString().ToLower();
        var value = property["value"];

        switch (propertyType)
        {
            case "duration":
                return lstAllRcd_FormatDurationValue(value.ToString());
            case "monetary_amount":
                return value as JObject ?? new JObject();
            // All other types (including address) are treated as simple values
            default:
                return value;
        }
    }

    /// <summary>
    /// Expands an ISO 8601 duration string for ListAllRecords helper outputs.
    /// </summary>
    private JObject lstAllRcd_FormatDurationValue(string isoDuration)
    {
        return CreateExpandedDurationObject(
            isoDuration
        );
    }

    /// <summary>
    /// Formats one clause property in the ListAllRecords response into the connector's
    /// clause helper object.
    /// </summary>
    private JObject lstAllRcd_FormatClauseProperty(string propertyName, JObject clause)
    {
        var clauseValue = clause["value"] as JObject;
        if (clauseValue == null)
            return new JObject();

        var displayName = propertyName;
        if (!displayName.EndsWith(" Clause", StringComparison.OrdinalIgnoreCase))
        {
            displayName += " Clause";
        }

        return new JObject
        {
            ["displayName"] = displayName,
            ["description"] = $"The {propertyName} clause.",
            ["clauseText"] = clauseValue["clauseText"],
            ["source"] = clauseValue["source"],
            ["clauseType"] = clauseValue["clauseType"],
            ["languagePosition"] = clauseValue["languagePosition"]
        };
    }

    // ################################################################################
    // Retrieve Record ################################################################
    // ################################################################################

    /// <summary>
    /// Expands a retrieved record into the connector's formatted schema, formatted values,
    /// attachment helpers, propertiesByTitle, and propertiesAsArray outputs.
    /// </summary>
    private JObject rtrRcd_TransformRetrieveRecord(JObject body)
    {
        var properties = body["properties"] as JObject;
        var attachments = body["attachments"] as JObject;

        var formattedSchema = new JObject();
        var formattedProperties = new JObject();

        if (properties != null)
        {
            formattedSchema["recordProperties"] = rtrRcd_FormatRecordPropertiesSchema(properties);
            formattedSchema["recordClauses"] = rtrRcd_FormatRecordClausesSchema(properties);
            formattedProperties["recordProperties"] = rtrRcd_FormatRecordProperties(properties);
            formattedProperties["recordClauses"] = rtrRcd_FormatRecordClauses(properties);
        }

        if (attachments != null)
        {
            formattedSchema["recordAttachments"] = rtrRcd_CreateAttachmentSchema(attachments);
            formattedProperties["recordAttachments"] = rtrRcd_FormatAttachments(attachments);
            body["attachmentsAsArray"] = rtrRcd_CreateAttachmentsArray(attachments);
        }

        // Create the new properties using helper functions.
        body["propertiesByTitle"] = rtrRcd_CreatePropertiesByTitle(formattedProperties["recordProperties"] as JObject);

        // Pass the formatted schema's properties so we can get type information.
        JObject recordSchemaProperties = (formattedSchema["recordProperties"]?["properties"] as JObject) ?? new JObject();
        body["propertiesAsArray"] = rtrRcd_CreatePropertiesAsArray(formattedProperties["recordProperties"] as JObject, recordSchemaProperties);

        body["formattedSchema"] = new JObject
        {
            ["type"] = "object",
            ["description"] = "OpenAPI Formatted Properties",
            ["x-ms-visibility"] = "important",
            ["properties"] = formattedSchema
        };
        body["formattedProperties"] = formattedProperties;

        return body;
    }

    /// <summary>
    /// Builds the propertiesByTitle helper object by mapping each record property display
    /// name to its formatted value.
    /// </summary>
    private JObject rtrRcd_CreatePropertiesByTitle(JObject recordProperties)
    {
        var propertiesByTitle = new JObject();
        if (recordProperties != null)
        {
            foreach (var prop in recordProperties.Properties())
            {
                // Retrieve displayName from schema info (or use the property name if not found).
                var schemaProperty = rtrRcd_GetRecordSchemaProperty(prop.Name);
                string title = schemaProperty?["displayName"]?.ToString() ?? prop.Name;
                propertiesByTitle[title] = prop.Value;
            }
        }
        return propertiesByTitle;
    }

    /// <summary>
    /// Builds the propertiesAsArray helper output with title, property name, description,
    /// type, and formatted value for each record property.
    /// </summary>
    private JArray rtrRcd_CreatePropertiesAsArray(JObject recordProperties, JObject recordSchemaProperties)
    {
        var propertiesAsArray = new JArray();
        if (recordProperties != null)
        {
            foreach (var prop in recordProperties.Properties())
            {
                // Retrieve display name and description from schema.
                var schemaProperty = rtrRcd_GetRecordSchemaProperty(prop.Name);
                string title = schemaProperty?["displayName"]?.ToString() ?? prop.Name;
                string description = schemaProperty?["description"]?.ToString() ?? $"The {prop.Name}.";
                string propertyType = "string";
                if (recordSchemaProperties != null && recordSchemaProperties[prop.Name]?["type"] != null)
                {
                    propertyType = recordSchemaProperties[prop.Name]["type"].ToString();
                }

                JObject propertyArrayItem = new JObject
                {
                    ["title"] = title,
                    ["property"] = prop.Name,
                    ["description"] = description,
                    ["type"] = propertyType,
                    ["value"] = prop.Value
                };
                propertiesAsArray.Add(propertyArrayItem);
            }
        }
        return propertiesAsArray;
    }

    /// <summary>
    /// Builds the formattedSchema recordProperties object for non-clause record fields.
    /// </summary>
    private JObject rtrRcd_FormatRecordPropertiesSchema(JObject properties)
    {
        var recordPropertiesSchema = new JObject();

        foreach (var property in properties.Properties())
        {
            if (!rtrRcd_IsClauseProperty(property))
            {
                rtrRcd_ParseRecordSchemaProperty(property, recordPropertiesSchema);
            }
        }

        return new JObject
        {
            ["title"] = "Properties",
            ["type"] = "object",
            ["description"] = "The properties of the record.",
            ["x-ms-visibility"] = "important",
            ["properties"] = recordPropertiesSchema
        };
    }

    /// <summary>
    /// Builds the formattedSchema recordClauses object for clause record fields.
    /// </summary>
    private JObject rtrRcd_FormatRecordClausesSchema(JObject properties)
    {
        var recordClausesSchema = new JObject();

        foreach (var property in properties.Properties())
        {
            if (rtrRcd_IsClauseProperty(property))
            {
                var clauseObject = rtrRcd_FormatClauseProperty(property);
                recordClausesSchema[property.Name] = rtrRcd_CreateClauseSchema(
                    clauseObject["displayName"].ToString(),
                    clauseObject["description"].ToString()
                );
            }
        }

        return new JObject
        {
            ["title"] = "Clauses",
            ["type"] = "object",
            ["description"] = "The clauses of the record.",
            ["x-ms-visibility"] = "important",
            ["properties"] = recordClausesSchema
        };
    }

    /// <summary>
    /// Formats the non-clause record property values for the formattedProperties output.
    /// </summary>
    private JObject rtrRcd_FormatRecordProperties(JObject properties)
    {
        var transformedProperties = new JObject();

        foreach (var property in properties.Properties())
        {
            if (!rtrRcd_IsClauseProperty(property))
            {
                var propertySchema = rtrRcd_GetRecordSchemaProperty(property.Name);
                transformedProperties[property.Name] = rtrRcd_FormatRecordPropertyValue(
                    property.Value as JObject,
                    propertySchema
                );
            }
        }

        return transformedProperties;
    }

    /// <summary>
    /// Formats the clause record property values for the formattedProperties output.
    /// </summary>
    private JObject rtrRcd_FormatRecordClauses(JObject properties)
    {
        var transformedClauses = new JObject();

        foreach (var property in properties.Properties())
        {
            if (rtrRcd_IsClauseProperty(property))
            {
                transformedClauses[property.Name] = rtrRcd_FormatClauseProperty(property);
            }
        }

        return transformedClauses;
    }

    /// <summary>
    /// Identifies whether a record property should be treated as a clause field.
    /// </summary>
    private bool rtrRcd_IsClauseProperty(JProperty property)
    {
        var propertyValue = property.Value as JObject;
        return propertyValue != null && propertyValue["type"]?.ToString().ToLower() == "clause";
    }

    /// <summary>
    /// Adds one non-clause record property to the formatted schema using the current
    /// record metadata definitions.
    /// </summary>
    private void rtrRcd_ParseRecordSchemaProperty(JProperty property, JObject formattedSchema)
    {
        var propertyValue = property.Value as JObject;
        if (propertyValue != null && propertyValue["type"] != null)
        {
            string propertyName = property.Name;
            string propertyType = propertyValue["type"].ToString().ToLower();

            var schemaProperty = rtrRcd_GetRecordSchemaProperty(propertyName);
            string displayName = schemaProperty?["displayName"]?.ToString() ?? propertyName;
            string description = schemaProperty?["description"]?.ToString() ?? $"The {propertyName}.";

            JObject formattedProperty = rtrRcd_ParseRecordPropertySchemaByType(
                propertyType,
                displayName,
                propertyName,
                description
            );
            formattedSchema[propertyName] = formattedProperty;
        }
    }

    /// <summary>
    /// Dispatches record property schema formatting based on the Ironclad property type.
    /// </summary>
    private JObject rtrRcd_ParseRecordPropertySchemaByType(string propertyType, string displayName, string propertyName, string description)
    {
        return FormatRecordPropertySchemaCore(
            propertyType,
            displayName,
            description,
            rtrRcd_FormatMonetaryAmountPropertySchema,
            rtrRcd_FormatDurationPropertySchema
        );
    }

    /// <summary>
    /// Builds the formatted schema used for record address properties.
    /// </summary>
    private JObject rtrRcd_FormatAddressPropertySchema(string displayName, string propertyName)
    {
        return new JObject
        {
            ["type"] = "object",
            ["title"] = displayName,
            ["description"] = $"The {displayName}.",
            ["x-ms-visibility"] = "important",
            ["properties"] = new JObject
            {
                ["lines"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "string" },
                    ["title"] = "Address Lines",
                    ["description"] = "The lines of the address."
                },
                ["locality"] = new JObject { ["type"] = "string", ["title"] = "Locality" },
                ["region"] = new JObject { ["type"] = "string", ["title"] = "Region" },
                ["postcode"] = new JObject { ["type"] = "string", ["title"] = "Postcode" },
                ["country"] = new JObject { ["type"] = "string", ["title"] = "Country" }
            }
        };
    }

    /// <summary>
    /// Builds the formatted schema used for record monetary amount properties.
    /// </summary>
    private JObject rtrRcd_FormatMonetaryAmountPropertySchema(string displayName, string description)
    {
        return new JObject
        {
            ["type"] = "object",
            ["title"] = displayName,
            ["description"] = description,
            ["x-ms-visibility"] = "important",
            ["properties"] = new JObject
            {
                ["amount"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Amount",
                    ["description"] = $"The amount of the {displayName}."
                },
                ["currency"] = new JObject
                {
                    ["type"] = "string",
                    ["title"] = "Currency",
                    ["description"] = $"The currency of the {displayName}."
                }
            }
        };
    }

    /// <summary>
    /// Builds the formatted schema used for record duration properties.
    /// </summary>
    private JObject rtrRcd_FormatDurationPropertySchema(string displayName, string description)
    {
        return new JObject
        {
            ["type"] = "object",
            ["title"] = displayName,
            ["description"] = description,
            ["x-ms-visibility"] = "important",
            ["properties"] = new JObject
            {
                ["isoDuration"] = new JObject
                {
                    ["type"] = "string",
                    ["title"] = "ISO Duration",
                    ["description"] = $"The ISO 8601 duration representation of the {displayName}."
                },
                ["years"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Years",
                    ["description"] = $"The years of the {displayName}."
                },
                ["months"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Months",
                    ["description"] = $"The months of the {displayName}."
                },
                ["weeks"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Weeks",
                    ["description"] = $"The weeks of the {displayName}."
                },
                ["days"] = new JObject
                {
                    ["type"] = "number",
                    ["title"] = "Days",
                    ["description"] = $"The days of the {displayName}."
                }
            }
        };
    }

    /// <summary>
    /// Builds the formatted schema used for primitive record property types.
    /// </summary>
    private JObject rtrRcd_FormatBasicPropertySchema(
        string propertyType,
        string displayName,
        string description
    )
    {
        var formattedProperty = new JObject
        {
            ["title"] = displayName,
            ["description"] = description,
            ["x-ms-visibility"] = "important"
        };

        switch (propertyType)
        {
            case "string":
            case "address": // Address is treated as string.
                formattedProperty["type"] = "string";
                break;
            case "number":
            case "integer":
                formattedProperty["type"] = "number";
                break;
            case "boolean":
                formattedProperty["type"] = "boolean";
                break;
            case "date":
                formattedProperty["type"] = "string";
                formattedProperty["format"] = "date-time";
                break;
            default:
                formattedProperty["type"] = "string";
                break;
        }

        return formattedProperty;
    }

    /// <summary>
    /// Looks up record property metadata captured earlier so later helpers can reuse the
    /// display name and description consistently.
    /// </summary>
    private JObject rtrRcd_GetRecordSchemaProperty(string propertyName)
    {
        if (recordSchemaInfo == null || !recordSchemaInfo.ContainsKey("properties"))
            return null;

        var properties = recordSchemaInfo["properties"] as JObject;
        if (properties == null || !properties.ContainsKey(propertyName))
            return null;

        var propertyInfo = properties[propertyName] as JObject;
        if (propertyInfo == null)
            return null;

        return new JObject
        {
            ["displayName"] = propertyInfo["displayName"] ?? propertyName,
            ["description"] = propertyInfo["description"] ?? $"The {propertyName}."
        };
    }

    /// <summary>
    /// Formats one record property value based on its property type.
    /// </summary>
    private JToken rtrRcd_FormatRecordPropertyValue(JObject propertyValue, JObject propertySchema)
    {
        if (propertyValue == null || !propertyValue.ContainsKey("type") || !propertyValue.ContainsKey("value"))
        {
            return null;
        }

        string propertyType = propertyValue["type"].ToString().ToLower();
        var value = propertyValue["value"];

        switch (propertyType)
        {
            case "monetary_amount":
                return rtrRcd_FormatMonetaryAmountPropertyValue(value as JObject);
            case "duration":
                return rtrRcd_FormatDurationPropertyValue(value.ToString());
            default:
                return value;
        }
    }

    /// <summary>
    /// Formats a record monetary amount value into the connector's amount/currency object.
    /// </summary>
    private JObject rtrRcd_FormatMonetaryAmountPropertyValue(JObject monetaryAmount)
    {
        if (monetaryAmount == null)
        {
            return new JObject();
        }

        return new JObject
        {
            ["amount"] = monetaryAmount["amount"],
            ["currency"] = monetaryAmount["currency"]
        };
    }

    /// <summary>
    /// Expands an ISO 8601 duration string for RetrieveRecord helper outputs.
    /// </summary>
    private JObject rtrRcd_FormatDurationPropertyValue(string isoDuration)
    {
        return CreateExpandedDurationObject(
            isoDuration
        );
    }

    /// <summary>
    /// Formats one record clause into the connector's clause helper object with display
    /// metadata and clause content.
    /// </summary>
    private JObject rtrRcd_FormatClauseProperty(JProperty property)
    {
        var clauseValue = (property.Value as JObject)?["value"] as JObject;
        if (clauseValue == null)
        {
            return new JObject();
        }

        var schemaProperty = rtrRcd_GetRecordSchemaProperty(property.Name);
        var displayName = schemaProperty?["displayName"]?.ToString() ?? property.Name;

        if (!displayName.EndsWith(" Clause", StringComparison.OrdinalIgnoreCase))
        {
            displayName += " Clause";
        }

        return new JObject
        {
            ["displayName"] = displayName,
            ["description"] = schemaProperty?["description"]?.ToString() ?? $"The {property.Name} clause.",
            ["clauseText"] = clauseValue["clauseText"],
            ["source"] = clauseValue["source"],
            ["clauseType"] = clauseValue["clauseType"],
            ["languagePosition"] = clauseValue["languagePosition"]
        };
    }

    /// <summary>
    /// Builds the formatted schema used for one record clause property.
    /// </summary>
    private JObject rtrRcd_CreateClauseSchema(string displayName, string description)
    {
        return new JObject
        {
            ["type"] = "object",
            ["title"] = displayName,
            ["description"] = description,
            ["x-ms-visibility"] = "important",
            ["properties"] = CreateClauseSchemaProperties(
                "The display name of the clause.",
                "The description of the clause.",
                "The text content of the clause.",
                "The source of the clause.",
                "The type of the clause.",
                "internal",
                "The language position of the clause.",
                "The type of language position."
            )
        };
    }

    /// <summary>
    /// The record schema routes expose clause objects with slightly different wording and
    /// visibility rules. This helper centralizes the shared object shape so both routes
    /// keep emitting the exact same connector contract.
    /// </summary>
    private JObject CreateClauseSchemaProperties(
        string displayNameDescription,
        string descriptionDescription,
        string clauseTextDescription,
        string sourceDescription,
        string clauseTypeDescription,
        string internalVisibility,
        string languagePositionDescription,
        string languagePositionTypeDescription
    )
    {
        return new JObject
        {
            ["displayName"] = CreateClauseSchemaTextField(
                "Display Name",
                displayNameDescription,
                null
            ),
            ["description"] = CreateClauseSchemaTextField(
                "Description",
                descriptionDescription,
                null
            ),
            ["clauseText"] = CreateClauseSchemaTextField(
                "Clause Text",
                clauseTextDescription,
                null
            ),
            ["source"] = CreateClauseSchemaTextField(
                "Source",
                sourceDescription,
                internalVisibility
            ),
            ["clauseType"] = CreateClauseSchemaTextField(
                "Clause Type",
                clauseTypeDescription,
                internalVisibility
            ),
            ["languagePosition"] = CreateClauseLanguagePositionSchema(
                languagePositionDescription,
                languagePositionTypeDescription,
                internalVisibility
            )
        };
    }

    /// <summary>
    /// Creates a string field schema used inside the shared clause schema builders.
    /// </summary>
    private JObject CreateClauseSchemaTextField(
        string title,
        string description,
        string visibility
    )
    {
        var field = new JObject
        {
            ["type"] = "string",
            ["title"] = title,
            ["description"] = description
        };

        if (!string.IsNullOrEmpty(visibility))
        {
            field["x-ms-visibility"] = visibility;
        }

        return field;
    }

    /// <summary>
    /// Creates the languagePosition object schema used inside clause helper outputs.
    /// </summary>
    private JObject CreateClauseLanguagePositionSchema(
        string description,
        string typeDescription,
        string visibility
    )
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["type"] = CreateClauseSchemaTextField("Type", typeDescription, null)
            }
        };

        if (!string.IsNullOrEmpty(description))
        {
            schema["title"] = "Language Position";
            schema["description"] = description;
        }

        if (!string.IsNullOrEmpty(visibility))
        {
            schema["x-ms-visibility"] = visibility;
        }

        return schema;
    }

    /// <summary>
    /// Builds a record attachment schema object. The two record routes that expose
    /// attachment schemas differ only in whether they include the displayName field and
    /// in the wording of the key description.
    /// </summary>
    private JObject CreateRecordAttachmentSchemaObject(
        string displayName,
        string description,
        bool includeDisplayNameField,
        string keyDescription
    )
    {
        var properties = new JObject
        {
            ["filename"] = new JObject
            {
                ["type"] = "string",
                ["title"] = "Filename",
                ["description"] = $"The filename of the {displayName}."
            },
            ["contentType"] = new JObject
            {
                ["type"] = "string",
                ["title"] = "Content Type",
                ["description"] = $"The content type of the {displayName}."
            },
            ["href"] = new JObject
            {
                ["type"] = "string",
                ["title"] = "Download URL",
                ["description"] = $"The download URL for the {displayName}."
            }
        };

        if (includeDisplayNameField)
        {
            properties["displayName"] = new JObject
            {
                ["type"] = "string",
                ["title"] = "Display Name",
                ["description"] = $"The display name of the {displayName}."
            };
        }

        properties["key"] = new JObject
        {
            ["type"] = "string",
            ["title"] = "Key",
            ["description"] = keyDescription
        };

        return new JObject
        {
            ["type"] = "object",
            ["title"] = displayName,
            ["description"] = description,
            ["x-ms-visibility"] = "important",
            ["properties"] = properties
        };
    }

    /// <summary>
    /// Builds the formattedSchema recordAttachments object for a retrieved record.
    /// </summary>
    private JObject rtrRcd_CreateAttachmentSchema(JObject attachments)
    {
        var attachmentProperties = new JObject();
        var attachmentSchemas = rtrRcd_GetAttachmentSchemas();
        if (attachmentSchemas != null)
        {
            foreach (var attachment in attachments.Properties())
            {
                var attachmentName = attachment.Name;
                var attachmentSchema = rtrRcd_GetAttachmentSchemaInfo(
                    attachmentSchemas,
                    attachmentName
                );
                var displayName = attachmentSchema?["displayName"]?.ToString() ?? attachmentName;
                var description =
                    attachmentSchema?["description"]?.ToString()
                    ?? $"The {displayName} attachment.";

                attachmentProperties[attachmentName] = CreateRecordAttachmentSchemaObject(
                    displayName,
                    description,
                    true,
                    $"The key of the {displayName}."
                );
            }
        }

        return new JObject
        {
            ["title"] = "Attachments",
            ["type"] = "object",
            ["description"] = "The attachments associated with the record.",
            ["x-ms-visibility"] = "important",
            ["properties"] = attachmentProperties
        };
    }

    /// <summary>
    /// Formats retrieved record attachments into the connector's attachment helper objects.
    /// </summary>
    private JObject rtrRcd_FormatAttachments(JObject attachments)
    {
        var formattedAttachments = new JObject();
        var attachmentSchemas = rtrRcd_GetAttachmentSchemas();
        if (attachmentSchemas != null)
        {
            foreach (var attachment in attachments.Properties())
            {
                var attachmentName = attachment.Name;
                var attachmentValue = attachment.Value as JObject;
                var attachmentSchema = rtrRcd_GetAttachmentSchemaInfo(
                    attachmentSchemas,
                    attachmentName
                );
                var displayName = attachmentSchema?["displayName"]?.ToString() ?? attachmentName;

                formattedAttachments[attachmentName] = new JObject
                {
                    ["filename"] = attachmentValue?["filename"],
                    ["contentType"] = attachmentValue?["contentType"],
                    ["href"] = attachmentValue?["href"],
                    ["displayName"] = displayName,
                    ["key"] = attachmentName
                };
            }
        }

        return formattedAttachments;
    }

    /// <summary>
    /// Builds the attachmentsAsArray helper output for a retrieved record.
    /// </summary>
    private JArray rtrRcd_CreateAttachmentsArray(JObject attachments)
    {
        var attachmentsArray = new JArray();
        var attachmentSchemas = rtrRcd_GetAttachmentSchemas();
        if (attachmentSchemas != null)
        {
            foreach (var attachment in attachments.Properties())
            {
                var attachmentName = attachment.Name;
                var attachmentSchema = rtrRcd_GetAttachmentSchemaInfo(
                    attachmentSchemas,
                    attachmentName
                );
                var displayName = attachmentSchema?["displayName"]?.ToString() ?? attachmentName;

                attachmentsArray.Add(
                    new JObject
                    {
                        ["name"] = attachmentName,
                        ["displayName"] = displayName,
                        ["key"] = attachmentName
                    }
                );
            }
        }

        return attachmentsArray;
    }

    /// <summary>
    /// Returns the cached record attachment metadata loaded before RetrieveRecord runs.
    /// </summary>
    private JObject rtrRcd_GetAttachmentSchemas()
    {
        return recordSchemaInfo?["attachments"] as JObject;
    }

    /// <summary>
    /// Returns the cached metadata entry for one record attachment name.
    /// </summary>
    private JObject rtrRcd_GetAttachmentSchemaInfo(JObject attachmentSchemas, string attachmentName)
    {
        return attachmentSchemas?[attachmentName] as JObject;
    }

    /// <summary>
    /// Preloads record schema information before RetrieveRecord runs so the later response
    /// formatter can emit the enriched schema and attachment structures expected today.
    /// </summary>
    private async Task rtrRcd_RetrieveRecordSchemaInformation()
    {
        var schemaResponse = await FetchJsonFromConnectorApiAsync(
                "/public/api/v1/records/metadata",
                "Failed to retrieve schema information."
            )
            .ConfigureAwait(false);

        this.recordSchemaInfo = new JObject
        {
            ["properties"] = schemaResponse["properties"],
            ["attachments"] = schemaResponse["attachments"]
        };

        this.Context.Logger.LogInformation(
            $"Retrieved schema information: {this.recordSchemaInfo?.ToString()}"
        );
    }

    // ################################################################################
    // Retrieve Email Thread ##########################################################
    // ################################################################################
    /// <summary>
    /// Simplifies email thread attachments into the connector's download-link and key
    /// shape while leaving the rest of the response untouched.
    /// </summary>
    private JObject rtrEml_TransformRetrieveEmailThread(JObject body)
    {
        var attachments = body["attachments"] as JArray;
        if (attachments != null)
        {
            var updatedAttachments = new JArray(
                attachments
                    .OfType<JObject>()
                    .Where(attachment => attachment.ContainsKey("download"))
                    .Select(
                        attachment =>
                        {
                            string downloadUrl = attachment["download"].ToString();
                            string key = rtrEml_ExtractKeyFromDownloadUrl(downloadUrl);
                            attachment["key"] = key;
                            return attachment;
                        }
                    )
            );
            body["attachments"] = updatedAttachments;
        }
        return body;
    }

    /// Extracts a key from the given download URL.
    private string rtrEml_ExtractKeyFromDownloadUrl(string downloadUrl)
    {
        var match = Regex.Match(downloadUrl, @"/document/([^/]+)/download");
        if (match.Success && match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }
        return string.Empty;
    }
}

using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// The response envelope every social endpoint answers in.
    ///
    /// Three rules, all read out of the client's own transport
    /// (docs/research/findings-social-api.md, section 2), all of which look like
    /// details and are not:
    ///
    /// 1. The HTTP status is ALWAYS 200 - including for creates and for failures.
    ///    HttpHelper.HandleResponseStatusCode returns early only on a literal 200
    ///    and otherwise throws, popping "Issue connecting to server. Please try
    ///    again in a bit!" at the player before the body is ever looked at. 201
    ///    Created and 204 No Content are errors to this client.
    ///
    /// 2. Because of (1), errors have to ride IN BAND on a 200, as
    ///    {"success": false, "errorCode": "..."}. The client's structured error
    ///    path only ever runs on a 200 response.
    ///
    /// 3. The body must always be a JSON OBJECT. The client does
    ///    JToken.Parse(body) and then jToken["statusCode"] = ..., which throws on
    ///    an array root and on an empty body - so even the endpoints that ignore
    ///    the payload need a full envelope.
    ///
    /// Fields we deliberately do NOT emit: statusCode and originalResponseData.
    /// Both exist on the client's ResponseSchema but are written by the client
    /// itself after parsing; sending them would be inventing wire fields.
    /// </summary>
    internal static class SocialEnvelope
    {
        /// <summary>A success carrying a single object at <c>data</c>.</summary>
        internal static JObject Ok(JToken data)
        {
            JObject envelope = new JObject { ["success"] = true };
            envelope["data"] = data ?? JValue.CreateNull();
            return envelope;
        }

        /// <summary>
        /// A success carrying a collection at <c>data.items</c>.
        ///
        /// This is the shape MOST list endpoints use - the client iterates
        /// model.data["items"]. It is NOT universal: see <see cref="OkBareList"/>.
        /// </summary>
        internal static JObject OkItems(JArray items)
        {
            return Ok(new JObject { ["items"] = items ?? new JArray() });
        }

        /// <summary>
        /// A success carrying a collection as a BARE ARRAY at <c>data</c>.
        ///
        /// Used by exactly one endpoint we serve, GET memberships/crew/{crewUid},
        /// whose client code iterates model.data directly rather than
        /// model.data["items"] (CrewServerImpl.cs:60) - while its immediate
        /// neighbour, GET memberships/invites/crew/{crewUid}, does use items
        /// (CrewServerImpl.cs:76). That is not a mistake in the reading; it is an
        /// inconsistency in the original service, and matching it is the whole
        /// job. Wrapping this one in items would make the crew panel show nobody.
        ///
        /// Note this still nests inside the envelope object, so rule (3) holds:
        /// the ARRAY is the value of data, not the root of the body.
        /// </summary>
        internal static JObject OkBareList(JArray items)
        {
            return Ok(items ?? new JArray());
        }

        /// <summary>
        /// A success with no payload, for the endpoints the client sends with
        /// <c>dataFieldExpected: false</c> and whose result it ignores.
        /// </summary>
        internal static JObject OkNoData()
        {
            return new JObject { ["success"] = true };
        }

        /// <summary>
        /// A refusal. <paramref name="errorCode"/> must come from
        /// <see cref="SocialErrorCodes"/> - anything else renders to the player as
        /// "Unknown error code: ...".
        /// </summary>
        internal static JObject Error(string errorCode)
        {
            return new JObject
            {
                ["success"] = false,
                ["errorCode"] = errorCode,
            };
        }
    }
}

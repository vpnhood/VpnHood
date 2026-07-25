using NJsonSchema;
using NJsonSchema.Generation;

namespace VpnHood.AppLib.Swagger;

// OpenApi3 marks a property required only when C# declares it 'required', so members that are
// merely non-nullable (computed properties, value types) become optional in the generated client.
// Non-nullable means the server always sends it, so mark it required and let nullability alone
// express "may be null".
internal class RequireNonNullablePropertiesSchemaProcessor : ISchemaProcessor
{
    public void Process(SchemaProcessorContext context)
    {
        foreach (var property in context.Schema.ActualProperties.Values)
            if (!property.IsNullable(SchemaType.OpenApi3))
                property.IsRequired = true;
    }
}

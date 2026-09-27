using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PaGetto.Core.Configuration;

public class HealthCheckOptions : IValidatableObject
{
    [Required]
    public string Path { get; set; } = "/health";

    /// <summary>
    /// What the overall status property is called in the health check response. Default is "Status".
    /// </summary>
    public string StatusPropertyName { get; set; } = "Status";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (! Path.StartsWith('/'))
        {
            yield return new ValidationResult(
                $"The {nameof(Path)} needs to start with a /",
                new[] { nameof(Path) });
        }
    }
}

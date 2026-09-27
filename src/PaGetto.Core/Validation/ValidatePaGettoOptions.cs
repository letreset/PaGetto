using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Validation;

/// <summary>
/// A configuration that validates options using data annotations.
/// </summary>
/// <typeparam name="TOptions">The type of options to validate.</typeparam>
public class ValidatePaGettoOptions<TOptions> : IValidateOptions<TOptions> where TOptions : class
{
    private readonly string _optionsName;

    /// <summary>
    /// Create a new validator.
    /// </summary>
    /// <param name="optionsName">
    /// The option's key in the configuration or appsettings.json file,
    /// or null if the options was created from the root configuration.
    /// </param>
    public ValidatePaGettoOptions(string optionsName)
    {
        _optionsName = optionsName;
    }

    public ValidateOptionsResult Validate(string name, TOptions options)
    {
        var context = new ValidationContext(options);
        var validationResults = new List<ValidationResult>();
        if (Validator.TryValidateObject(options, context, validationResults, validateAllProperties: true))
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        var message = (_optionsName == null)
            ? $"Invalid configs"
            : $"Invalid '{_optionsName}' configs";

        foreach (var result in validationResults)
        {
            errors.Add($"{message}: {result}");
        }

        return ValidateOptionsResult.Fail(errors);
    }
}

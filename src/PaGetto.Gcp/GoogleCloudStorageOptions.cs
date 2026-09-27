using System.ComponentModel.DataAnnotations;
using PaGetto.Core.Configuration;

namespace PaGetto.Gcp;

public class GoogleCloudStorageOptions : StorageOptions
{
    [Required]
    public string BucketName { get; set; }
}

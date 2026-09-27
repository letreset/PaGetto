using System.ComponentModel.DataAnnotations;

namespace PaGetto.Tencent;

public class TencentStorageOptions
{
    [Required]
    public string AppId { get; set; } = string.Empty;
    [Required]
    public string SecretId { get; set; } = string.Empty;
    [Required]
    public string SecretKey { get; set; } = string.Empty;
    [Required]
    public string Region { get; set; } = string.Empty;
    [Required]
    public string BucketName { get; set; } = string.Empty;
    public int KeyDurationSecond { get; set; } = 600;

}

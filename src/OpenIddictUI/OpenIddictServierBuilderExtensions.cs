using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenIddictUI;

public static class OpenIddictServierBuilderExtensions
{
    public static OpenIddictServerBuilder AddSigningCredential(
        this OpenIddictServerBuilder builder,
        string? filename = null)
    {
        filename ??= Path.Combine(Directory.GetCurrentDirectory(), "cert");
        var privateKeyPath = Path.Combine(Directory.GetCurrentDirectory(), $"{filename}.pem");
        X509Certificate2 certificate;
        if (File.Exists(privateKeyPath))
        {
            var pem = File.ReadAllText(privateKeyPath);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem); // 加载私钥（自动带公钥）
            certificate = LoadRsa256Certificate(rsa);
        }
        else
        {
            using var rsa = RSA.Create(2048);
            certificate = LoadRsa256Certificate(rsa);
            using var privateKey = certificate.GetRSAPrivateKey();
            if (privateKey == null)
            {
                throw new InvalidOperationException();
            }

            var privateKeyBase64 = Convert.ToBase64String(privateKey.ExportPkcs8PrivateKey(),
                Base64FormattingOptions.InsertLineBreaks);
            File.WriteAllText(privateKeyPath,
                $"-----BEGIN PRIVATE KEY-----\n{privateKeyBase64}\n-----END PRIVATE KEY-----");
        }

        builder.AddEncryptionCertificate(certificate);
        builder.AddSigningCertificate(certificate);
        return builder;
    }

    /// <summary>
    /// 自动生成 2048 位 RSA 自签名证书（X509Certificate2）
    /// </summary>
    private static X509Certificate2 LoadRsa256Certificate(RSA rsa, string commonName = "CN=OpenId")
    {
        // 创建证书请求
        var request = new CertificateRequest(
            subjectName: commonName, // 证书名称
            key: rsa, // RSA 密钥
            hashAlgorithm: HashAlgorithmName.SHA256, // RS256
            padding: RSASignaturePadding.Pkcs1);

        // 设置证书有效期（10年）
        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddYears(100);

        // 创建自签名证书
        var cert = request.CreateSelfSigned(notBefore, notAfter);
        return cert;
    }
}
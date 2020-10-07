using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using ImageMagick;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ThumbnailService
{
    public class Function
    {
        IAmazonS3 S3Client { get; set; }

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
        {
            S3Client = new AmazonS3Client();
        }

        /// <summary>
        /// Constructs an instance with a preconfigured S3 client. This can be used for testing the outside of the Lambda environment.
        /// </summary>
        /// <param name="s3Client"></param>
        public Function(IAmazonS3 s3Client)
        {
            this.S3Client = s3Client;
        }
        
        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
        /// to respond to S3 notifications.
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandler(S3Event evnt, ILambdaContext context)
        {
            var s3Event = evnt.Records?[0].S3;
            string thumnailResultBucket = "thumbnail-result-s3-bucket";
            if (s3Event == null)
            {
                return null;
            }

            try
            {
                var thumbnailStream =await GenerateThumbnail(s3Event.Bucket.Name, s3Event.Object.Key, context);
                var thumbnailKey =await UploadThumbnail(thumnailResultBucket, s3Event.Object.Key, thumbnailStream);
                string res = $"Thumbnail saved to s3://{thumnailResultBucket}/{thumbnailKey}";
                context.Logger.LogLine(res);
                return res;

            }
            catch (Exception e)
            {
                context.Logger.LogLine($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}. Make sure they exist and your bucket is in the same region as this function.");
                context.Logger.LogLine(e.Message);
                context.Logger.LogLine(e.StackTrace);
                throw;
            }
        }

        private async Task<string> UploadThumbnail(string bucketName,string key, MemoryStream thumbnailImageStream)
        {
            var index = key.LastIndexOf('/');
            var thumbnailKey = "thumbnails/" + (index != -1 ? key.Substring(index + 1) : key);
            await this.S3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = thumbnailKey,
                InputStream = thumbnailImageStream
            });

            return thumbnailKey;
        }

        private async Task<MemoryStream> GenerateThumbnail(string bucketName, string key,ILambdaContext context)
        {

            MemoryStream resizedImageStream;
            using (var response = await this.S3Client.GetObjectAsync(bucketName, key))
            {
                context.Logger.Log("Magick Image Resize");
                try
                {
                    MagickImage magickImage = new MagickImage();
                    using (MagickImage image = new MagickImage(response.ResponseStream))
                    {
                        image.Resize(200, 200);
                        context.Logger.LogLine($"Image resized");

                        resizedImageStream = new MemoryStream();
                        image.Write(resizedImageStream);
                        resizedImageStream.Position = 0;
                        return resizedImageStream;
                    }
                }
                catch (Exception e)
                {
                    context.Logger.Log(e.Message);
                    throw;
                }
            }
        }
    }
}

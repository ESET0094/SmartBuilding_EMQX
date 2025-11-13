using System.Net.Mail;
namespace Email
{
    internal class Program
    {
        static void Main(string[] args)
        {
            MailMessage message = new MailMessage();
            message.From = new MailAddress("sender@example.com");
            message.To.Add("recipient@example.com");
            message.Subject = "Usage Limit Reached";
            message.Body = $"Your Usage {usage} is reached. Please recharge for uninterrupted usage";
            message.IsBodyHtml = true;
            SmtpClient smtpClient = new SmtpClient("smtp.gmail.com");
            smtpClient.Port = 587; // Common port for TLS/STARTTLS
            smtpClient.EnableSsl = true; // Enable SSL for secure connection
            smtpClient.Credentials = new System.Net.NetworkCredential("your_username", "your_password");

            try
            {
                smtpClient.Send(message);
                Console.WriteLine("Email Sent Successfully");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending email: {ex.Message}");

            }
        }
    }
}

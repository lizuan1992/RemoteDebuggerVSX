using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Text.RegularExpressions;

namespace RemoteAD7Engine
{
    [ComVisible(true)]
    [Guid("AD7056E1-0F4F-4D79-83A5-9AF4A1A7A4B0")]
    public class AD7TextTypeViewer : IDebugCustomViewer
    {
        public int DisplayValue(IntPtr hwnd, uint dwID, object pHostServices, IDebugProperty3 pProperty)
        {
            try
            {
                // Get the string value
                var info = new DEBUG_PROPERTY_INFO[1];
                pProperty.GetPropertyInfo(enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE, 10, 0, null, 0, info);
                string value = info[0].bstrValue;

                ShowTextDialog("Text Viewer", value);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open text viewer: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return VSConstants.S_OK;
        }

        private static void ShowTextDialog(string title, string content)
        {
            // Ensure line breaks are in Windows format (\r\n)
            content = Regex.Replace(content, @"(?<!\r)\n", "\r\n");

            using (var form = new Form())
            {
                form.Text = title;
                form.Size = new Size(600, 400);
                var textBox = new TextBox
                {
                    Multiline = true,
                    Dock = DockStyle.Fill,
                    Text = content,
                    ScrollBars = ScrollBars.Both,
                    ReadOnly = true
                };
                form.Controls.Add(textBox);
                form.ShowDialog();
            }
        }
    }

    [ComVisible(true)]
    [Guid("AD7056E2-0F4F-4D79-83A5-9AF4A1A7A4B0")]
    public class AD7AutoTypeViewer : IDebugCustomViewer
    {
        public int DisplayValue(IntPtr hwnd, uint dwID, object pHostServices, IDebugProperty3 pProperty)
        {
            try
            {
                // Get the string value
                var info = new DEBUG_PROPERTY_INFO[1];
                pProperty.GetPropertyInfo(enum_DEBUGPROP_INFO_FLAGS.DEBUGPROP_INFO_VALUE, 10, 0, null, 0, info);
                string value = info[0].bstrValue;

                // Detect content type
                if (value.TrimStart().StartsWith("<") && value.TrimEnd().EndsWith(">"))
                {
                    // Possible XML or HTML
                    if (value.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // HTML - display in browser
                        ShowHtmlDialog(value);
                    }
                    else
                    {
                        // XML - display as formatted text
                        ShowTextDialog("XML Viewer", value);
                    }
                }
                else if ((value.TrimStart().StartsWith("{") && value.TrimEnd().EndsWith("}")) ||
                         (value.TrimStart().StartsWith("[") && value.TrimEnd().EndsWith("]")))
                {
                    // Possible JSON
                    try
                    {
                        var obj = Newtonsoft.Json.Linq.JToken.Parse(value);
                        string formatted = obj.ToString(Newtonsoft.Json.Formatting.Indented);
                        ShowTextDialog("JSON Viewer", formatted);
                    }
                    catch
                    {
                        // If not valid JSON, display as plain text
                        ShowTextDialog("Text Viewer", value);
                    }
                }
                else
                {
                    // Default to plain text
                    ShowTextDialog("Text Viewer", value);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open auto type viewer: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return VSConstants.S_OK;
        }

        private static void ShowTextDialog(string title, string content)
        {
            // Ensure line breaks are in Windows format (\r\n)
            content = Regex.Replace(content, @"(?<!\r)\n", "\r\n");

            using (var form = new Form())
            {
                form.Text = title;
                form.Size = new Size(600, 400);
                var textBox = new TextBox
                {
                    Multiline = true,
                    Dock = DockStyle.Fill,
                    Text = content,
                    ScrollBars = ScrollBars.Both,
                    ReadOnly = true
                };
                form.Controls.Add(textBox);
                form.ShowDialog();
            }
        }

        private static void ShowHtmlDialog(string content)
        {
            using (var form = new Form())
            {
                form.Text = "HTML Viewer";
                form.Size = new Size(800, 600);
                var browser = new WebBrowser
                {
                    Dock = DockStyle.Fill
                };
                browser.DocumentText = content;
                form.Controls.Add(browser);
                form.ShowDialog();
            }
        }
    }
}
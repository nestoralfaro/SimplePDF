using System;
using System.Collections.Generic;
using System.Linq;

namespace SimplePDF
{
    /// <summary>
    /// Represents the PDF document as the structure with its objects.
    /// </summary>
    public class PDFDocument
    {
        private Catalog _catalog;
        private List<Font> _fontList;
        private List<Base64JPGStream> _base64JPGStreamList;
        private Pages _pages;

        public string DestinationDirectory { get; }
        public string Name { get; }
        public string FullPath { get; }
        public int PageCount => _pages.Entries.Count;
        public PDFDocument(string destinationDirectory, string name)
        {
            _pages = new Pages();
            _catalog = new Catalog(_pages);
            _fontList = new List<Font>();
            _base64JPGStreamList = new List<Base64JPGStream>();
            DestinationDirectory = destinationDirectory;
            (FullPath, Name) = Utils.UniquePath(destinationDirectory, Utils.SanitizeFileName(name));
        }
        /// <summary>
        /// Track a new font in the document.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="baseFont"></param>
        /// <returns>A reference to the new font as a dictionary object.</returns>
        public Font NewFont(string name, string baseFont)
        {
            Font font = new Font(name, baseFont);
            _fontList.Add(font);
            return font;
        }
        /// <summary>
        /// Track a new Base64 JPG image in the document.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="base64JPG"></param>
        /// <param name="scale"></param>
        /// <param name="translation"></param>
        /// <returns>A reference to a Base64JPGStream dictionary object.</returns>
        public Base64JPGStream NewBase64JPG(string name, string base64JPG, (uint horizontal, uint vertical) scale, (uint x, uint y) translation)
        {
            Base64JPGXObject xObjBase64JPG = new Base64JPGXObject(name, base64JPG);
            Base64JPGStream qBase64JPG = new Base64JPGStream(xObjBase64JPG, scale, translation);
            _base64JPGStreamList.Add(qBase64JPG);
            return qBase64JPG;
        }
        /// <summary>
        /// Track a new page in the document.
        /// </summary>
        /// <param name="resources"></param>
        /// <returns>A StringBuilder to write a stream dictionary object for the content of the new page.</returns>
        public System.Text.StringBuilder NewPage(List<DictionaryObject> resources)
        {
            Page page = new Page();
            ContentStream stream = new ContentStream();
            page.AddContentRef(stream);
            foreach (DictionaryObject dictionaryObject in resources)
            {
                if (dictionaryObject is Font)
                {
                    page.AddFontRef(dictionaryObject as Font);
                }
                else if (dictionaryObject is Base64JPGStream)
                {
                    page.AddXobjRef((dictionaryObject as Base64JPGStream).XObjBase64JPG);
                    page.AddContentRef(dictionaryObject);
                }
            }
            _pages.Add(page);
            return stream.Content;
        }
        /// <summary>
        /// Write the pdf structure to the file.
        /// </summary>
        public void Create()
        {
            #region Assign Object Numbers
            uint objCount = 0;
            _catalog.Number = ++objCount;
            _pages.Number = ++objCount;
            foreach (Page page in _pages.Entries)
            {
                page.Parent = _pages.Number;
                page.Number = ++objCount;
                foreach (DictionaryObject dictionaryObject in page.ContentRefs)
                {
                    if (dictionaryObject is ContentStream)
                    {
                        dictionaryObject.Number = ++objCount;
                    }
                }
            }
            foreach (Font font in _fontList)
            {
                font.Number = ++objCount;
            }
            foreach (Base64JPGStream base64JPGStream in _base64JPGStreamList)
            {
                base64JPGStream.Number = ++objCount;
                base64JPGStream.XObjBase64JPG.Number = ++objCount;
            }
            #endregion Assign Object Numbers
            #region Write To File
            List<int> xrefOffsets = new List<int>();
            int size;
            int offSet = 0;

            if (System.IO.File.Exists(FullPath)) System.IO.File.Delete(FullPath);
            System.IO.FileStream file = new System.IO.FileStream(FullPath, System.IO.FileMode.Append);
            string header = "%PDF-1.7\n";
            file.Write(Utils.StringToUTF8Bytes(out size, header), 0, size);
            offSet += size;
            xrefOffsets.Add(offSet);

            file.Write(_catalog.BuildObject(out size), 0, size);
            offSet += size;
            xrefOffsets.Add(offSet);

            file.Write(_pages.BuildObject(out size), 0, size);
            offSet += size;
            xrefOffsets.Add(offSet);
            foreach (Page page in _pages.Entries)
            {
                file.Write(page.BuildObject(out size), 0, size);
                offSet += size;
                xrefOffsets.Add(offSet);
                foreach (DictionaryObject dictionaryObject in page.ContentRefs)
                {
                    if (dictionaryObject is ContentStream)
                    {
                        file.Write(dictionaryObject.BuildObject(out size), 0, size);
                        offSet += size;
                        xrefOffsets.Add(offSet);
                    }
                }
            }

            foreach (Font font in _fontList)
            {
                file.Write(font.BuildObject(out size), 0, size);
                offSet += size;
                xrefOffsets.Add(offSet);
            }

            foreach (Base64JPGStream qBase64JPG in _base64JPGStreamList)
            {
                file.Write(qBase64JPG.BuildObject(out size), 0, size);
                offSet += size;
                xrefOffsets.Add(offSet);

                file.Write(qBase64JPG.XObjBase64JPG.BuildObject(out size), 0, size);
                offSet += size;
                xrefOffsets.Add(offSet);
            }

            int startxref = xrefOffsets[xrefOffsets.Count - 1];
            xrefOffsets.RemoveAt(xrefOffsets.Count - 1);
            int manyObjs = xrefOffsets.Count + 1;

            string xref = $"xref\n0 {manyObjs}\n0000000000 65535 f\n{ string.Join("\n", xrefOffsets.Select(o => $"{o.ToString().PadLeft(10, '0')} 00000 n")) }\n";
            file.Write(Utils.StringToUTF8Bytes(out size, xref), 0, size);
            string trailer = $"trailer <<\n/Root {_catalog.Number} 0 R\n/Size {manyObjs}\n>>\nstartxref\n{startxref}\n%%EOF";
            file.Write(Utils.StringToUTF8Bytes(out size, trailer), 0, size);
            file.Close();
            #endregion Write To File
        }
    }
    #region DictionaryObjects
    /// <summary>
    /// Represents the common features of dictionary objects in a pdf structure.
    /// </summary>
    public abstract class DictionaryObject
    {

        public uint Number { get; set; }
        /// <summary>
        /// Build the dictionary object in the form `{Number} 0 obj << >> endobj`
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public abstract byte[] BuildObject(out int size);
    }
    /// <summary>
    /// Represents a catalog dictionary object.
    /// </summary>
    public class Catalog : DictionaryObject
    {
        public Pages PagesReference { get; }
        public Catalog(Pages pagesReference)
        {
            PagesReference = pagesReference;
        }
        public override byte[] BuildObject(out int size) => Utils.StringToUTF8Bytes(out size, $"{Number} 0 obj <<\n/Type /Catalog\n/Pages {PagesReference.Number} 0 R\n>> endobj\n");
    }
    /// <summary>
    /// Represents a pages dictionary object.
    /// </summary>
    public class Pages : DictionaryObject
    {
        private List<Page> _entries;

        public IReadOnlyList<Page> Entries => _entries.AsReadOnly();
        public Pages()
        {
            _entries = new List<Page>();
        }
        public override byte[] BuildObject(out int size) => Utils.StringToUTF8Bytes(out size, $"{Number} 0 obj <<\n/Type /Pages\n/Count {_entries.Count}\n/Kids [ {string.Join(" ", _entries.Select(p => $"{p.Number} 0 R"))} ]\n>> endobj\n");
        public void Add(Page page) => _entries.Add(page);
    }
    /// <summary>
    /// Represents a page dictionary object.
    /// </summary>
    public class Page : DictionaryObject
    {
        private List<Font> _fontRefs;
        private List<Base64JPGXObject> _xobjRefs;
        private List<DictionaryObject> _contentRefs;

        public uint Parent { get; set; }
        public IReadOnlyList<DictionaryObject> ContentRefs => _contentRefs.AsReadOnly();
        public void AddContentRef(DictionaryObject s) => _contentRefs.Add(s);
        public void AddFontRef(Font f) => _fontRefs.Add(f);
        public void AddXobjRef(Base64JPGXObject x) => _xobjRefs.Add(x);
        public Page()
        {
            _contentRefs = new List<DictionaryObject>();
            _fontRefs = new List<Font>();
            _xobjRefs = new List<Base64JPGXObject>();
        }
        public override byte[] BuildObject(out int size) => Utils.StringToUTF8Bytes(out size, $"{Number} 0 obj <<\n/Type /Page\n/Contents [ {string.Join(" ", _contentRefs.Select(s => $"{s.Number} 0 R"))} ]\n/MediaBox [ 0 0 612 792 ]\n/Parent {Parent} 0 R\n/Resources << /Font << {string.Join(" ", _fontRefs.Select(f => $"/{f.Alias} {f.Number} 0 R"))} >>{(_xobjRefs.Count != 0 ? $" /XObject << {string.Join(" ", _xobjRefs.Select(x => $"/{x.Name} {x.Number} 0 R"))} >>" : string.Empty)} >>\n>> endobj\n");
    }
    /// <summary>
    /// Represents a font dictionary object.
    /// </summary>
    public class Font : DictionaryObject
    {
        public string Alias { get; }
        public string BaseFont { get; }
        public Font(string alias, string baseFont)
        {
            Alias = alias;
            BaseFont = baseFont;
        }
        public override byte[] BuildObject(out int size) => Utils.StringToUTF8Bytes(out size, $"{Number} 0 obj <<\n/Type /Font\n/BaseFont /{BaseFont}\n/Encoding /WinAnsiEncoding\n/Subtype /Type1\n>> endobj\n");
    }
    /// <summary>
    /// Represents the XObject with a stream containing the Base64JPG bytes.
    /// </summary>
    public class Base64JPGXObject : DictionaryObject
    {
        private int _width { get; }
        private int _height { get; }
        private byte[] _imageByteStream { get; }

        public string Name { get; }
        public override byte[] BuildObject(out int size)
        {
            byte[] startBytes = System.Text.Encoding.UTF8.GetBytes($"{Number} 0 obj <<\n/Name /{Name}\n/Type /XObject\n/Subtype /Image\n/Width {_width}\n/Height {_height}\n/Length {_imageByteStream.Length}\n/Filter /DCTDecode\n/ColorSpace /DeviceRGB\n/BitsPerComponent 8\n>> stream\n");
            byte[] endBytes = System.Text.Encoding.UTF8.GetBytes("\nendstream\nendobj\n");
            size = startBytes.Length + _imageByteStream.Length + endBytes.Length;
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream(size))
            {
                using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(ms))
                {
                    writer.Write(startBytes);
                    writer.Write(_imageByteStream);
                    writer.Write(endBytes);
                }
                return ms.ToArray();
            }
        }
        public Base64JPGXObject(string name, string base64JPG)
        {
            Name = name;
            _imageByteStream = Convert.FromBase64String(base64JPG);
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream(_imageByteStream))
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(ms))
                {
                    _width = bmp.Width;
                    _height = bmp.Height;
                }
            }
        }
    }
    /// <summary>
    /// Represents a content stream object that paints the XObject containing the Base64JPG bytes stream.
    /// </summary>
    public class Base64JPGStream : DictionaryObject
    {
        private (uint horizontal, uint vertical) _scale;
        private (uint horizontal, uint vertical) _translation;
        public Base64JPGXObject XObjBase64JPG;
        public override byte[] BuildObject(out int size)
        {
            string s = $"\nq {_scale.horizontal} 0 0 {_scale.vertical} {_translation.horizontal} {_translation.vertical} cm /{XObjBase64JPG.Name} Do Q\n";
            return Utils.StringToUTF8Bytes(out size, $"{Number} 0 obj <<\n/Length {s.Length}\n>> stream{s}endstream\nendobj\n");
        }
        public Base64JPGStream(Base64JPGXObject xObjBase64JPG, (uint horizontal, uint vertical) scale, (uint horizontal, uint vertical) translation)
        {
            XObjBase64JPG = xObjBase64JPG;
            _scale = scale;
            _translation = translation;
        }
    }
    /// <summary>
    /// Represents a simple content stream object to be used in a page.
    /// </summary>
    public class ContentStream : DictionaryObject
    {
        public System.Text.StringBuilder Content { get; }
        public ContentStream()
        {
            Content = new System.Text.StringBuilder();
        }
        public override byte[] BuildObject(out int size) => Utils.StringToUTF8Bytes(out size, $"{Number} 0 obj <<\n/Length {Content.Length}\n>>\nstream{Content}endstream\nendobj\n");
    }
    #endregion DictionaryObjects
    /// <summary>
    /// Common utilities used throughout the library.
    /// </summary>
    internal static class Utils
    {
        /// <summary>
        /// Converts a string to a UTF-8 encoded byte array.
        /// </summary>
        /// <param name="size">The size of the resulting byte array</param>
        /// <param name="content">The string to convert</param>
        /// <returns>A byte array containing the UTF-8 encoded string</returns>
        /// <exception cref="ArgumentException">Thrown when the content is null or empty.</exception>
        /// <exception cref="Exception">Thrown when an unspecified error occurs.</exception>
        public static byte[] StringToUTF8Bytes(out int size, string content)
        {
            size = 0;
            if (string.IsNullOrEmpty(content))
            {
                throw new ArgumentException("Content cannot be null or empty", nameof(content));
            }
            byte[] utf8Buff = System.Text.Encoding.UTF8.GetBytes(content);
            size = utf8Buff.Length;
            return utf8Buff;
        }
        /// <summary>
        /// Ensures that the returned path is unique by overwriting the existing one or appending a repeating count to the file name.
        /// </summary>
        /// <param name="destinationDirectory"></param>
        /// <param name="pdfName"></param>
        /// <returns>The tuple (string pdfPath, string pdfName)</returns>
        public static (string pdfPath, string pdfName) UniquePath(string destinationDirectory, string pdfName)
        {
            int fileCount = 1;
            string pdfPath = System.IO.Path.ChangeExtension(System.IO.Path.Combine(destinationDirectory, pdfName), "pdf");
            while (System.IO.File.Exists(pdfPath))
            {
                try
                {
                    System.IO.File.Delete(pdfPath);
                    //Console.WriteLine($"File \"{pdfPath}\" has been removed to be replaced.");
                }
                catch
                {
                    string newPdfName = $"{pdfName}_{++fileCount}";
                    string newPdfPath = System.IO.Path.ChangeExtension(System.IO.Path.Combine(destinationDirectory, newPdfName), "pdf");
                    //Console.WriteLine($"Unable to remove existing file \"{pdfPath}\". Renaming new file to \"{newPdfPath}\"");
                    pdfName = newPdfName;
                    pdfPath = newPdfPath;
                }
            }
            return (pdfPath, pdfName);
        }
        /// <summary>
        /// Ensures that the file name does not contain invalid characters.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string SanitizeFileName(string name)
        {
            string invalidChars = System.Text.RegularExpressions.Regex.Escape(new string(System.IO.Path.GetInvalidFileNameChars()));
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            return System.Text.RegularExpressions.Regex.Replace(name, invalidRegStr, "_");
        }
    }
}

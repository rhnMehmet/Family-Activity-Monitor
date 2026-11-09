using FamilyMonitor.Core.Security;

namespace FamilyMonitor.Agent;

internal static class ParentPinUi
{
    public static bool Authorize(IWin32Window owner,ParentalPinService service)=>!service.Enabled||Verify(owner,service,false)is not null;
    private static string? Verify(IWin32Window owner,ParentalPinService service,bool recovery)
    {
        using var dialog=new Form{Text=recovery?"PIN kurtarma kodu":"Ebeveyn ayarları için PIN",ClientSize=new Size(510,220),StartPosition=FormStartPosition.CenterParent,Font=new Font("Segoe UI",10),MinimizeBox=false,MaximizeBox=false};
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(15)};
        layout.Controls.Add(new Label{Text=recovery?"Tek seferlik kurtarma kodunuzu girin. PIN kaldırılır.":"PIN'i girin. Yanlış 5 denemede 15 dakika beklenir.",AutoSize=true,MaximumSize=new Size(470,0)});
        var input=new TextBox{UseSystemPasswordChar=true,MaxLength=128,Width=460};layout.Controls.Add(input);
        var feedback=new Label{AutoSize=true,MaximumSize=new Size(470,0)};layout.Controls.Add(feedback);
        var accept=new Button{Text=recovery?"Kodu doğrula ve PIN'i sıfırla":"Doğrula",AutoSize=true};layout.Controls.Add(accept);dialog.Controls.Add(layout);
        string? verified=null;
        accept.Click+=async(_,_)=>{
            accept.Enabled=false;var value=input.Text;input.Clear();
            try
            {
                var result=await Task.Run(()=>recovery?service.Recover(value):service.Verify(value));
                if(dialog.IsDisposed)return;
                if(result.Success){verified=value;dialog.DialogResult=DialogResult.OK;dialog.Close();}
                else feedback.Text=result.Locked?"Çok sayıda yanlış deneme. 15 dakikalık bekleme dolmadan tekrar deneyemezsiniz.":$"Doğrulanamadı. Kalan deneme: {result.AttemptsRemaining}";
            }
            catch(Exception e)when(e is not OutOfMemoryException){if(!dialog.IsDisposed)feedback.Text="İşlem yapılamadı ("+e.GetType().Name+").";}
            finally{if(!dialog.IsDisposed)accept.Enabled=true;}
        };
        dialog.AcceptButton=accept;return dialog.ShowDialog(owner)==DialogResult.OK?verified:null;
    }
    public static void Recover(IWin32Window owner,ParentalPinService service)
    {
        if(!service.Enabled){MessageBox.Show(owner,"PIN etkin değil.");return;}
        if(Verify(owner,service,true)is not null)MessageBox.Show(owner,"PIN kaldırıldı. İzleme izinleri ve geçmiş değiştirilmedi. Yeni PIN'i ebeveyn ayarlarından oluşturabilirsiniz.");
    }
    public static void Show(IWin32Window owner,ParentalPinService service)
    {
        var current=service.Enabled?Verify(owner,service,false):"";
        if(current is null)return;
        using var dialog=new Form{Text="Ebeveyn ayarları / PIN",ClientSize=new Size(580,340),StartPosition=FormStartPosition.CenterParent,Font=new Font("Segoe UI",10)};
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(15)};
        layout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(540,0),Text="PIN yalnızca ebeveyn ayarlarını ve otomatik başlatmayı etkinleştirmeyi korur. İzlemeyi durdurma, izin geri çekme ve çıkış her zaman serbesttir. Aynı Windows hesabının dosyalarına erişebilen kişi bu yerel korumayı aşabilir; işletim sistemi politikası değildir."});
        layout.Controls.Add(new Label{Text="Yeni PIN (6–12 rakam):",AutoSize=true});
        var first=new TextBox{UseSystemPasswordChar=true,MaxLength=12,Width=300};layout.Controls.Add(first);
        layout.Controls.Add(new Label{Text="Yeni PIN'i tekrar girin:",AutoSize=true});
        var second=new TextBox{UseSystemPasswordChar=true,MaxLength=12,Width=300};layout.Controls.Add(second);
        var save=new Button{Text=service.Enabled?"PIN'i değiştir":"PIN oluştur",AutoSize=true};
        var disable=new Button{Text="PIN korumasını kaldır",AutoSize=true,Enabled=service.Enabled};
        var row=new FlowLayoutPanel{AutoSize=true};row.Controls.AddRange([save,disable]);layout.Controls.Add(row);
        var message=new Label{AutoSize=true,MaximumSize=new Size(540,0)};layout.Controls.Add(message);dialog.Controls.Add(layout);
        save.Click+=async(_,_)=>{
            if(first.Text!=second.Text){message.Text="PIN tekrarları eşleşmiyor.";return;}
            var value=first.Text;first.Clear();second.Clear();save.Enabled=disable.Enabled=false;
            try
            {
                var code=await Task.Run(()=>service.Enabled?service.Change(current,value):service.Setup(value));
                if(dialog.IsDisposed)return;
                if(code is null){message.Text="Mevcut PIN doğrulanamadı; ayar değiştirilmedi.";return;}
                ShowRecovery(dialog,code);dialog.Close();
            }
            catch(Exception e)when(e is not OutOfMemoryException){if(!dialog.IsDisposed)message.Text=e is ArgumentException?"PIN 6–12 rakam olmalıdır.":"PIN kaydedilemedi ("+e.GetType().Name+").";}
            finally{if(!dialog.IsDisposed){save.Enabled=true;disable.Enabled=service.Enabled;}}
        };
        disable.Click+=async(_,_)=>{
            disable.Enabled=false;
            try{if(await Task.Run(()=>service.Disable(current))&&!dialog.IsDisposed)dialog.Close();}
            catch(Exception e)when(e is not OutOfMemoryException){if(!dialog.IsDisposed)message.Text="PIN kaldırılamadı ("+e.GetType().Name+").";}
            finally{if(!dialog.IsDisposed)disable.Enabled=true;}
        };
        dialog.ShowDialog(owner);
    }
    private static void ShowRecovery(IWin32Window owner,string code)
    {
        using var dialog=new Form{Text="PIN kurtarma kodu — yalnızca şimdi gösterilir",ClientSize=new Size(620,250),StartPosition=FormStartPosition.CenterParent,Font=new Font("Segoe UI",10)};
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(15)};
        layout.Controls.Add(new Label{Text="Bu kodu güvenli, ayrı bir yerde saklayın. Dosyaya veya loga yazılmaz. Kod unutulursa sessiz PIN atlama yoktur.",AutoSize=true,MaximumSize=new Size(580,0)});
        layout.Controls.Add(new TextBox{Text=code,ReadOnly=true,Multiline=true,Width=580,Height=60});
        var saved=new CheckBox{Text="Kurtarma kodunu güvenli bir yerde sakladım.",AutoSize=true};layout.Controls.Add(saved);
        var done=new Button{Text="Tamam",Enabled=false,AutoSize=true};saved.CheckedChanged+=(_,_)=>done.Enabled=saved.Checked;done.Click+=(_,_)=>dialog.Close();layout.Controls.Add(done);dialog.Controls.Add(layout);dialog.ShowDialog(owner);
    }
}

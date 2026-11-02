xaml_path = r'd:\01. NGUYEN TAN TAI\CODING AI\00.BAI 1_TOOLS\TaiAiAddin\Views\ElectricalPanelDialog.xaml'
with open(xaml_path, 'r', encoding='utf-8') as f:
    lines = f.readlines()

new_lines = []
i = 0
while i < len(lines):
    line = lines[i]

    # Subtitle
    if 'Thiết kế / Shopdrawing / BKL' in line and 'DOL' in line:
        new_lines.append('                            <TextBlock Text="Thiết kế / Shopdrawing / BKL • Cân pha tự động • Y-Δ/VFD/SS • Sụt áp TCVN • ATS • Riser" FontSize="11" Foreground="#666" />\n')
        i += 1
        continue

    # Buttons in row 0
    if '<Button x:Name="btnExportExcel"' in line:
        # replace the stackpanel content around it
        # find the end of this stackpanel
        btn_replacement = '''                        <Button x:Name="btnImportExcel" Content="📥 Nhập Excel" 
                                Style="{StaticResource OutlineBtn}" Click="BtnImportExcel_Click" Margin="0,0,5,0" FontWeight="SemiBold" Foreground="#007ACC" ToolTip="Nhập danh sách tải từ file Excel (.xls) hoặc CSV"/>
                        <Button x:Name="btnExportSampleTemplate" Content="📄 Mẫu Excel" 
                                Style="{StaticResource OutlineBtn}" Click="BtnExportSampleTemplate_Click" Margin="0,0,5,0" FontWeight="SemiBold" ToolTip="Tạo file Excel mẫu chuẩn"/>
                        <Button x:Name="btnExportExcel" Content="📊 Xuất Excel (.xls)" 
                                Style="{StaticResource OutlineBtn}" Click="BtnExportExcel_Click" Margin="0,0,5,0" FontWeight="SemiBold" Foreground="#1E7E34" ToolTip="Xuất bảng tính tải và Sheet bóc tách BOQ"/>
                        <Button x:Name="btnDrawRiser" Content="🌳 Cây Riser" 
                                Style="{StaticResource OutlineBtn}" Click="BtnDrawRiser_Click" Margin="0,0,5,0" FontWeight="SemiBold" Foreground="#6A1B9A" ToolTip="Vẽ sơ đồ cây phân phối toàn công trình"/>
                        <Button x:Name="btnOptimizeVoltageDrop" Content="⚡ Tối Ưu Cáp" 
                                Style="{StaticResource OutlineBtn}" Click="BtnOptimizeVoltageDrop_Click" Margin="0,0,5,0" FontWeight="SemiBold" Foreground="#D9534F" ToolTip="Tự động tăng cỡ cáp nếu vượt sụt áp TCVN"/>
                        <Button x:Name="btnCalculate" Content="⚡ Cân Pha Tự Động" 
                                Style="{StaticResource PrimaryBtn}" Click="BtnCalculate_Click"/>
'''
        new_lines.append(btn_replacement)
        # skip until </StackPanel>
        while i < len(lines) and '</StackPanel>' not in lines[i]:
            i += 1
        continue

    # After cmbDefaultCableType, add Incomer, Capacitor, Title Block, Front View
    if '</ComboBox>' in line and i > 150 and i < 185:
        new_lines.append(line)
        opts = '''
                        <!-- Nguồn vào Incomer -->
                        <TextBlock Text="Nguồn:" FontWeight="SemiBold" VerticalAlignment="Center" Margin="8,0,4,0"/>
                        <ComboBox x:Name="cboIncomer" Width="105" Height="26" VerticalContentAlignment="Center"
                                  SelectionChanged="CboIncomer_SelectionChanged" FontWeight="SemiBold" FontSize="11">
                            <ComboBoxItem Content="1 Nguồn EVN" Tag="SingleNormal" IsSelected="True"/>
                            <ComboBoxItem Content="2 Nguồn ATS" Tag="DualATS"/>
                            <ComboBoxItem Content="Liên Động 2 CB" Tag="DualInterlock"/>
                        </ComboBox>

                        <!-- Tụ bù -->
                        <CheckBox x:Name="chkHasCapacitor" Content="Tụ Bù" IsChecked="False" FontWeight="SemiBold" VerticalAlignment="Center" Margin="8,0,6,0"
                                  Checked="ChkCapacitor_Changed" Unchecked="ChkCapacitor_Changed" Foreground="#0277BD" ToolTip="Tính chọn và vẽ lộ Tụ bù công suất phản kháng"/>

                        <!-- Khung A3/A2 -->
                        <CheckBox x:Name="chkIncludeTitleBlock" Content="Khung A3/A2" IsChecked="True" FontWeight="SemiBold" VerticalAlignment="Center" Margin="0,0,6,0"
                                  Checked="ChkTitleBlock_Changed" Unchecked="ChkTitleBlock_Changed" Foreground="#333" ToolTip="Lồng khung bản vẽ kỹ thuật &amp; Khung tên"/>

                        <!-- Mặt ngoài tủ -->
                        <CheckBox x:Name="chkDrawFrontView" Content="Mặt Ngoài Tủ" IsChecked="True" FontWeight="SemiBold" VerticalAlignment="Center"
                                  Checked="ChkFrontView_Changed" Unchecked="ChkFrontView_Changed" Foreground="#7B1FA2" ToolTip="Vẽ phác thảo mặt ngoài cánh tủ điện (Front View)"/>
'''
        new_lines.append(opts)
        i += 1
        continue

    # Replace DOL column with Starter & Control & Length & Voltage Drop
    if 'Header="Bộ DOL"' in line or 'Header="B\u1ed9 DOL"' in line or '<!-- Cột Tùy chọn Bộ DOL -->' in line:
        # replace the whole template column
        grid_replacement = '''                            <!-- Cột Bộ Khởi Động -->
                            <DataGridTemplateColumn Header="Khởi Động" Width="85">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <TextBlock Text="{Binding StarterDisplay}" FontWeight="Bold" Foreground="#E65100" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                                <DataGridTemplateColumn.CellEditingTemplate>
                                    <DataTemplate>
                                        <ComboBox SelectedValue="{Binding StarterDisplay, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                                  SelectedValuePath="Tag" Height="22" VerticalContentAlignment="Center" FontSize="11">
                                            <ComboBoxItem Content="-" Tag="-"/>
                                            <ComboBoxItem Content="DOL" Tag="DOL" FontWeight="Bold" Foreground="#E65100"/>
                                            <ComboBoxItem Content="Y - Δ (2x3C)" Tag="Y - Δ" FontWeight="Bold" Foreground="#007ACC"/>
                                            <ComboBoxItem Content="VFD Biến Tần" Tag="VFD" FontWeight="Bold" Foreground="#2E7D32"/>
                                            <ComboBoxItem Content="Soft Starter" Tag="Soft Starter" FontWeight="Bold" Foreground="#6A1B9A"/>
                                        </ComboBox>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellEditingTemplate>
                            </DataGridTemplateColumn>

                            <!-- Cột Chế Độ Điều Khiển -->
                            <DataGridTemplateColumn Header="Điều Khiển" Width="85">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <TextBlock Text="{Binding ControlDisplay}" FontWeight="SemiBold" Foreground="#005A9E" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                                <DataGridTemplateColumn.CellEditingTemplate>
                                    <DataTemplate>
                                        <ComboBox SelectedValue="{Binding ControlDisplay, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                                  SelectedValuePath="Tag" Height="22" VerticalContentAlignment="Center" FontSize="11">
                                            <ComboBoxItem Content="-" Tag="-"/>
                                            <ComboBoxItem Content="H-O-A" Tag="H-O-A" FontWeight="Bold" Foreground="#005A9E"/>
                                            <ComboBoxItem Content="Phao (FS)" Tag="Phao (FS)" FontWeight="Bold" Foreground="#0277BD"/>
                                            <ComboBoxItem Content="Áp suất (PS)" Tag="Áp suất (PS)" FontWeight="Bold" Foreground="#D81B60"/>
                                        </ComboBox>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellEditingTemplate>
                            </DataGridTemplateColumn>

                            <!-- Cột Chiều Dài L (m) -->
                            <DataGridTextColumn Header="Dài L (m)" Binding="{Binding CableLength, StringFormat={}{0:0.#}, UpdateSourceTrigger=LostFocus}" Width="60"/>

                            <!-- Cột Sụt Áp ΔU % -->
                            <DataGridTemplateColumn Header="Sụt Áp ΔU" Width="70">
                                <DataGridTemplateColumn.CellTemplate>
                                    <DataTemplate>
                                        <TextBlock Text="{Binding VoltageDropDisplay}" 
                                                   Foreground="{Binding VoltageDropColorBrush}" 
                                                   FontWeight="Bold" 
                                                   HorizontalAlignment="Right" 
                                                   VerticalAlignment="Center"/>
                                    </DataTemplate>
                                </DataGridTemplateColumn.CellTemplate>
                            </DataGridTemplateColumn>
'''
        new_lines.append(grid_replacement)
        # skip lines until </DataGridTemplateColumn>
        while i < len(lines) and '</DataGridTemplateColumn>' not in lines[i]:
            i += 1
        i += 1 # skip </DataGridTemplateColumn>
        continue

    # Hint text
    if "* Chú thích: 3 Pha (Đỏ) - 1 Pha (Xanh lá). Cột 'Bộ DOL'" in line:
        new_lines.append("                                <TextBlock Text=\"* Chú thích: 3 Pha (Đỏ) - 1 Pha (Xanh lá) • Khởi động (DOL, Y-Δ, VFD, SS) • Điều khiển (HOA, FS, PS) • Sụt áp tự động cảnh báo màu đỏ.\" VerticalAlignment=\"Center\" Foreground=\"#555\" FontSize=\"11\" FontStyle=\"Italic\"/>\n")
        i += 1
        continue

    new_lines.append(line)
    i += 1

with open(xaml_path, 'w', encoding='utf-8') as f:
    f.writelines(new_lines)

print("XAML updated perfectly!")

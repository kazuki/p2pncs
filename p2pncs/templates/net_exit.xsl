<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />

	<xsl:template name="_title">終了 :: p2pncs</xsl:template>
 
	<xsl:template match="/page">
		<xsl:choose>
			<xsl:when test="/page/@exit = 'exit'">
				<h1>終了中...</h1>
				<div>プログラムを終了中です。</div>
				<div>GUIを利用している場合は、通知領域からアイコンが削除されることを以て、プログラムの終了が完了します。</div>
				<div>また、プログラム終了後は、このページには繋がらなくなりますので、このページは閉じてください。</div>
				<div style="margin-top: 1em; font-size: x-small">注: プログラムの終了にはしばらく時間がかかる場合がありますが、数分待っても終了しない場合は強制終了させてください。</div>
			</xsl:when>
			<xsl:otherwise>
				<h1>終了確認</h1>
				<p>本当に終了してもよろしいですか？</p>
				<form method="post" action="/net/exit">
					<div><input type="submit" value="終了する" /></div>
				</form>
			</xsl:otherwise>
		</xsl:choose>
	</xsl:template>
</xsl:stylesheet>

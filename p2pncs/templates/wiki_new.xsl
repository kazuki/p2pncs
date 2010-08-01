<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns="http://www.w3.org/1999/xhtml" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
	<xsl:import href="base.xsl" />
	<xsl:import href="validation.xsl" />
	<xsl:import href="mergeablefile_new.xsl" />

	<xsl:template name="_title">新規作成 :: Wiki :: p2pncs</xsl:template>
	<xsl:template name="_css">
		<xsl:call-template name="create_validation_stylesheet" />
	</xsl:template>
 
	<xsl:template match="/page">
		<h1>Wikiの新規作成</h1>
		<xsl:if test="$confirm_mode">
			<p>以下の内容でWikiを作成してもよろしいですか？</p>
		</xsl:if>
		<xsl:if test="$success_mode">
			<p>以下の内容でWikiを作成しました。</p>
			<p>
				<xsl:element name="a">
					<xsl:attribute name="href">
						<xsl:text>/</xsl:text>
						<xsl:value-of select="created/@key" />
					</xsl:attribute>
					<xsl:text>このリンク</xsl:text>
				</xsl:element>
				<xsl:text>をクリックすると作成したWikiへ移動します。</xsl:text>
			</p>
		</xsl:if>
		<form method="post" action="/wiki/new">
			<div>
				<xsl:call-template name="check_validation" />
			</div>
			<table>
				<tr>
					<td class="header">タイトル: </td>
					<td>
						<xsl:call-template name="create_input_with_validation">
							<xsl:with-param name="size">70</xsl:with-param>
							<xsl:with-param name="name">title</xsl:with-param>
						</xsl:call-template>
					</td>
				</tr>
				<tr>
					<td class="header">認証サーバ: </td>
					<td>
						<xsl:call-template name="create_authserver_form" />
					</td>
				</tr>
				<tr>
					<td colspan="2"><hr /></td>
				</tr>
				<tr>
					<td colspan="2" align="center">
						<xsl:if test="$confirm_mode">
							<input type="submit" value="再編集" name="re-edit" />
						</xsl:if>
						<xsl:if test="not($success_mode)">
							<input type="submit" value="作成" />
						</xsl:if>
					</td>
				</tr>
			</table>
		</form>
	</xsl:template>
</xsl:stylesheet>

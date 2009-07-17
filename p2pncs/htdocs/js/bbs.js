$(function() {
	$("#postButton").click(function() {
		$.create_post_dialog ("/bbs/" + $("#postKey").val(), {
			"name": $("#postName").val(),
			"body": $("#postBody").val(),
			"auth": $("#authsvr").val()
		}, function () {
			$("#postBody").val("");
			window.location.reload ();
		}, function () {});
	});
});
